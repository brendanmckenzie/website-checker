using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WebsiteChecker
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Run(new Uri(args[0])).Wait();
        }

        static async Task Run(Uri baseUri)
        {
            Console.WriteLine(baseUri);
            Console.WriteLine("---------");

            var processed = new List<Uri>();
            var processList = new List<Uri>();
            processList.Add(baseUri);
            var results = new List<PageInfo>();

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var nextList = processList;
            while (nextList.Any())
            {
                processList = nextList;
                nextList = new List<Uri>();

                foreach (var uri in processList)
                {
                    lock (processed)
                    {
                        processed.Add(uri);
                    }

                    var info = await GetPageInfo(uri);

                    lock (results)
                    {
                        results.Add(info);
                    }

                    Console.Write(info);

                    lock (processed)
                    {
                        lock (nextList)
                        {
                            foreach (var link in info.Links)
                            {
                                if (!processed.Contains(link) && !nextList.Contains(link))
                                {
                                    nextList.Add(link);
                                }
                            }
                        }
                    }
                };
            }


            stopwatch.Stop();
            var elapsed = (double)stopwatch.ElapsedMilliseconds / 1000.0;
            var average = (double)stopwatch.ElapsedMilliseconds / processed.Count;

            Console.WriteLine("---------");
            Console.WriteLine($"Processed: {processed.Count} links, total time: {elapsed:N0}s, average response: {average:N0}ms");

            OutputReport(results);

            Console.WriteLine("done.");
        }

        static void OutputReport(IEnumerable<PageInfo> pages)
        {
            var problems = pages.Where(ent => ent.ResponseCode != 200);
            if (problems.Any())
            {
                Console.WriteLine("Problems");
                Console.WriteLine("--------");
                foreach (var grp in problems.GroupBy(ent => ent.ResponseCode))
                {
                    Console.WriteLine($" {grp.Key}");

                    foreach (var ent in grp)
                    {
                        Console.WriteLine($"   {ent.Url}");
                    }

                    Console.WriteLine();
                }
            }

            var slowest = pages.FirstOrDefault(ent => ent.LoadTime == pages.Select(ent2 => ent2.LoadTime).Max());
            Console.WriteLine("Slowest page");
            Console.WriteLine(slowest);
        }

        static async Task<PageInfo> GetPageInfo(Uri url)
        {
            Console.WriteLine(" ");
            using (var client = new HttpClient())
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                try
                {
                    var baseAddress = new UriBuilder(url)
                    {
                        Path = string.Empty,
                        Query = string.Empty
                    };

                    client.BaseAddress = baseAddress.Uri;

                    var request = new HttpRequestMessage(HttpMethod.Get, url.PathAndQuery);

                    var res = await client.SendAsync(request);

                    var source = await res.Content.ReadAsStringAsync();

                    var contentType = new[] { "unknown" }.AsEnumerable();
                    res.Headers.TryGetValues("Content-Type", out contentType);

                    stopwatch.Stop();

                    return new PageInfo
                    {
                        Url = url,
                        ResponseCode = (int)res.StatusCode,
                        LoadTime = stopwatch.ElapsedMilliseconds,
                        ContentType = contentType?.FirstOrDefault(),
                        Links = GetLinks(url, source)
                    };
                }
                catch (WebException ex)
                {
                    stopwatch.Stop();

                    return new PageInfo
                    {
                        Url = url,
                        ContentType = ex.Response.ContentType,
                        LoadTime = stopwatch.ElapsedMilliseconds,
                        ResponseCode = (int)(ex.Response as HttpWebResponse).StatusCode,
                        Links = Enumerable.Empty<Uri>()
                    };

                }
            }
        }

        static IEnumerable<Uri> GetLinks(Uri originalUrl, string source)
        {
            const string pattern = "<(a).*?href=(\"|')(.+?)(\"|').*?>";

            var ret = Regex.Matches(source, pattern)
                .Cast<Match>()
                .Select(ent => ent.Groups[3].Value)
                .Where(ent => ent.StartsWith("/") || ent.StartsWith("http"))
                .Select(ent =>
                {
                    try
                    {
                        if (ent.StartsWith("/"))
                        {
                            return new Uri(originalUrl, new Uri(ent, UriKind.Relative));
                        }
                        else
                        {
                            return new Uri(ent);
                        }
                    }
                    catch
                    {
                        return null;
                        // problem URL...
                    }
                })
                .Where(ent => ent != null)
                .Distinct()
                .Where(ent => ent != originalUrl)
                .Where(ent => !(ent.ToString().EndsWith("jpg")))
                .Where(ent => !(ent.ToString().EndsWith("png")))
                .Where(ent => !(ent.ToString().EndsWith("mp3")))
                .Where(ent => !(ent.ToString().EndsWith("pdf")))
                .Where(ent => ent.Host == originalUrl.Host);

            return ret;
        }
    }

    class PageInfo
    {
        public int ResponseCode { get; set; }
        public string ContentType { get; set; }
        public long LoadTime { get; set; }

        public Uri Url { get; set; }

        public IEnumerable<Uri> Links { get; set; }

        public override string ToString()
        {
            var bufferWidth = 80; // Console.BufferWidth
            var width = bufferWidth - 42;
            var totalWidth = bufferWidth;

            var fields = new List<Tuple<string, int>>();
            fields.Add(new Tuple<string, int>(Url?.ToString(), -1));
            fields.Add(new Tuple<string, int>(" ", 1));
            fields.Add(new Tuple<string, int>(ResponseCode.ToString(), 5));
            fields.Add(new Tuple<string, int>(ContentType?.Split(';').First(), 15));
            fields.Add(new Tuple<string, int>($"{LoadTime,5:N0}ms", 8));
            fields.Add(new Tuple<string, int>($"{Links?.Count(),4:N0} links", 10));

            var expandoWidth = -1;
            var hasExpando = fields.Select(ent => ent.Item2).Count(ent => ent == -1) == 1;
            if (hasExpando)
            {
                var totalSpecified = fields.Select(ent => ent.Item2).Where(ent => ent != -1).Sum();
                expandoWidth = totalWidth - totalSpecified;
            }

            var ret = new StringBuilder();
            foreach (var field in fields)
            {
                var val = field.Item1?.ToString() ?? string.Empty;
                var len = field.Item2;
                if (len == -1) { len = expandoWidth; }

                if (val.Length > len)
                {
                    val = "..." + val.Substring(val.Length - len + 3);
                }

                if (val.Length < len)
                {
                    val = val + new string(' ', len - val.Length);
                }

                ret.Append(val);
            }

            return ret.ToString();
        }
    }
}
