using AngleSharp;

namespace Crawler
{
    internal class MyCrawler
    {

        private static readonly HttpClient SharedClient = new()
        {
            BaseAddress = new Uri("https://books.toscrape.com/"),
        };
        static async Task Main(string[] args)
        {
            var response = await SharedClient.GetAsync("");
            response.EnsureSuccessStatusCode();

            string html = await response.Content.ReadAsStringAsync();
            //print the response status e.g 200-ok
            Console.WriteLine(response.StatusCode.ToString() + "\n");

            //Check numbers of characters being returned from html
            Console.WriteLine(html.Length);
            //first 300 characters returned
            Console.WriteLine(html.Substring(0, Math.Min(300, html.Length)));

            //Parse HTML
            using var context = BrowsingContext.New(Configuration.Default);
            using var doc = await context.OpenAsync(req => req.Content(html));

            Console.WriteLine(doc.Title);
            var books = doc.QuerySelectorAll("article.product_pod");
            Console.WriteLine($"Book on page {books.Length}");

            var firstLink = doc.QuerySelector("article.product_pod h3 a");
            Console.WriteLine($"first book: {firstLink?.GetAttribute("title")}");

            var bookPrice = doc.QuerySelector("article.product_pod p.price_color");
            
            Console.WriteLine($"Book price: {bookPrice?.TextContent}");
        }
        
    }
}
