using System.Text;
using System.Text.Json;
using AngleSharp;

namespace Crawler
{
    internal record Book(string Title, string Price, string Url);

    internal class MyCrawler
    {
        private static readonly HttpClient SharedClient = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MyCrawler/1.0");
            return client;
        }

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            const int maxPages = 50;

            var start = new Uri("https://books.toscrape.com/");
            var disallowed = await LoadDisallowedAsync(start);

            var queue = new Queue<Uri>();
            var seen = new HashSet<string>();
            var allBooks = new List<Book>();

            queue.Enqueue(start);
            seen.Add(start.ToString());

            // The CSV is opened once and written to after every page.
            // AutoFlush pushes each line to disk immediately, so a crash loses nothing.
            using var csv = new StreamWriter("books.csv", false, new UTF8Encoding(true))
            {
                AutoFlush = true
            };
            csv.WriteLine("Title,Price,Url");

            int pages = 0;
            try
            {
                while (queue.Count > 0 && pages < maxPages)
                {
                    var url = queue.Dequeue();

                    if (IsDisallowed(url, disallowed))
                    {
                        Console.WriteLine($"Skipped (robots.txt): {url}");
                        continue;
                    }

                    Console.WriteLine($"Visiting: {url}");
                    string? html = await FetchHtmlAsync(url);
                    pages++;

                    if (html != null)
                    {
                        var (books, next) = await ParseAsync(html, url);
                        allBooks.AddRange(books);
                        AppendCsv(csv, books);

                        if (next != null && seen.Add(next.ToString()))
                        {
                            queue.Enqueue(next);
                        }
                    }

                    await Task.Delay(500);
                }
            }
            finally
            {
                // Runs whether the loop finished normally or crashed.
                Console.WriteLine($"Pages attempted: {pages}");
                Console.WriteLine($"Total books: {allBooks.Count}");
                SaveJson(allBooks, "books.json");
                Console.WriteLine($"CSV written to: {Path.GetFullPath("books.csv")}");
            }
        }

        static void AppendCsv(StreamWriter writer, List<Book> books)
        {
            foreach (var b in books)
            {
                writer.WriteLine($"{Csv(b.Title)},{Csv(b.Price)},{Csv(b.Url)}");
            }
        }

        static void SaveJson(List<Book> books, string path)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            string json = JsonSerializer.Serialize(books, options);
            File.WriteAllText(path, json, new UTF8Encoding(false));

            Console.WriteLine($"JSON written to: {Path.GetFullPath(path)}");
        }

        static string Csv(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        static async Task<string?> FetchHtmlAsync(Uri url)
        {
            const int maxAttempts = 3;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                bool retryable;

                try
                {
                    using var response = await SharedClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadAsStringAsync();
                    }

                    int code = (int)response.StatusCode;
                    Console.WriteLine($"  Failed: {code} {response.StatusCode}");

                    // 429 (slow down) and 5xx (server trouble) are temporary.
                    // Everything else (404, 403, ...) is not worth retrying.
                    retryable = code == 429 || code >= 500;
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"  Network error: {ex.Message}");
                    retryable = true;
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("  Timed out");
                    retryable = true;
                }

                if (!retryable || attempt == maxAttempts)
                {
                    return null;
                }

                // Exponential backoff: wait 1s, then 2s, doubling each time.
                int waitSeconds = (int)Math.Pow(2, attempt - 1);
                Console.WriteLine($"  Retrying in {waitSeconds}s (attempt {attempt + 1} of {maxAttempts})");
                await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
            }

            return null;
        }

        static async Task<List<string>> LoadDisallowedAsync(Uri site)
        {
            var rules = new List<string>();

            string? text = await FetchHtmlAsync(new Uri(site, "/robots.txt"));
            if (text == null) return rules;

            bool appliesToUs = false;
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();

                if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
                {
                    appliesToUs = line.Substring("User-agent:".Length).Trim() == "*";
                }
                else if (appliesToUs && line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase))
                {
                    var path = line.Substring("Disallow:".Length).Trim();
                    if (path.Length > 0) rules.Add(path);
                }
            }

            return rules;
        }

        static bool IsDisallowed(Uri url, List<string> rules)
        {
            return rules.Any(rule => url.AbsolutePath.StartsWith(rule, StringComparison.Ordinal));
        }

        static async Task<(List<Book> Books, Uri? Next)> ParseAsync(string html, Uri pageUrl)
        {
            using var context = BrowsingContext.New(Configuration.Default);
            using var doc = await context.OpenAsync(req => req.Content(html));

            var books = new List<Book>();
            foreach (var item in doc.QuerySelectorAll("article.product_pod"))
            {
                var link = item.QuerySelector("h3 a");
                string title = link?.GetAttribute("title") ?? "";
                string price = item.QuerySelector("p.price_color")?.TextContent ?? "";
                string href = link?.GetAttribute("href") ?? "";
                string absolute = new Uri(pageUrl, href).ToString();
                books.Add(new Book(title, price, absolute));
            }

            Uri? next = null;
            var nextHref = doc.QuerySelector("li.next a")?.GetAttribute("href");
            if (nextHref != null)
            {
                next = new Uri(pageUrl, nextHref);
            }

            return (books, next);
        }
    }
}