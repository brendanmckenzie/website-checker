using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WebsiteChecker
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Run(new Uri(args[0]));
        }

        static void Run(Uri baseUri)
        {
            Console.WriteLine(baseUri);
            Console.WriteLine("---------");

            var processed = new List<Uri>();
            var processList = new Queue<Uri>();
            processList.Enqueue(baseUri);
            var results = new List<PageInfo>();

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            while (processList.Count > 0)
            {
                var uri = processList.Dequeue();
                processed.Add(uri);

                var info = GetPageInfo(uri);

                results.Add(info);

                Console.Write(info);

                foreach (var link in info.Links)
                {
                    if (!processed.Contains(link) && !processList.Contains(link))
                    {
                        processList.Enqueue(link);
                    }
                }
            }
            stopwatch.Stop();
            var elapsed = (double)stopwatch.ElapsedMilliseconds / 1000.0;

            Console.WriteLine("---------");
            Console.WriteLine($"Processed: {processed.Count} links, total time: {elapsed:N0}");

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

        static PageInfo GetPageInfo(Uri url)
        {
            using (var client = new WebClient())
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                try
                {
                    var source = client.DownloadString(url);

                    stopwatch.Stop();

                    return new PageInfo
                    {
                        Url = url,
                        ResponseCode = 200, // TODO: ... this
                        LoadTime = stopwatch.ElapsedMilliseconds,
                        ContentType = client.ResponseHeaders["Content-Type"],
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
                .Select(ent => {
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
            var width = Console.BufferWidth - 42;

            var url = Url.ToString().Length > (width - 2) ? ("..." + Url.ToString().Substring(Url.ToString().Length - (width - 5))) : Url.ToString();
            url += new string(' ', width - url.Length);

            return $"{url} {ResponseCode,5} {ContentType.Split(';').First(),15} {LoadTime,5:N0}ms {Links?.Count(),5:N0} links";
        }
    }
}
