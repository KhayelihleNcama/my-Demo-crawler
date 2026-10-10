using AngleSharp;

namespace Crawler
{
    internal class MyCrawler
    {
        internal record Book(string Title, string Price, string Url);

        private static readonly HttpClient SharedClient = new()
        {
            BaseAddress = new Uri("https://books.toscrape.com/"),
        };
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var response = await SharedClient.GetAsync("");
            response.EnsureSuccessStatusCode();

            string html = await response.Content.ReadAsStringAsync();
            //print the response status e.g 200-ok
            Console.WriteLine(response.StatusCode.ToString() + "\n");

            //Check numbers of characters being returned from html
            //Console.WriteLine(html.Length);
            //first 300 characters returned
            //Console.WriteLine(html.Substring(0, Math.Min(300, html.Length)));

            //Parse HTML
            using var context = BrowsingContext.New(Configuration.Default);
            using var doc = await context.OpenAsync(req => req.Content(html));
            var baseuri = SharedClient.BaseAddress!;
            var results = new List<Book>();

            foreach (var book in doc.QuerySelectorAll("article.product_pod"))
            {
                var link = book.QuerySelector("h3 a");
                string title = link?.GetAttribute("title") ?? "";
                string price = book.QuerySelector("p.price_color") ?. TextContent ?? "";

                string relative = link?.GetAttribute("href") ?? "";

                string url = new Uri(baseuri,relative).ToString();
                results.Add(new Book(title,price,url));

            }

            Console.WriteLine($"Books found: {results.Count}");
            foreach (var b in results)
            {
                Console.WriteLine($"{b.Title} - {b.Price} - {b.Url} \n");
            }
        }
        
    }
}
