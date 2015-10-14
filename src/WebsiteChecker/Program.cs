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

            while (processList.Count > 0)
            {
                var uri = processList.Dequeue();
                processed.Add(uri);

                var info = GetPageInfo(uri);

                Console.Write(info);

                foreach (var link in info.Links)
                {
                    if (!processed.Contains(link) && !processList.Contains(link))
                    {
                        processList.Enqueue(link);
                    }
                }
            }
            Console.WriteLine("---------");
            Console.WriteLine($"Processed: {processed.Count} links");
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
                    if (ent.StartsWith("/"))
                    {
                        return new Uri(originalUrl, new Uri(ent, UriKind.Relative));
                    }
                    else
                    {
                        return new Uri(ent);
                    }
                })
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

            var url = Url.ToString().Length > (width - 2) ? ("..." + Url.ToString().Substring(0, (width - 5))) : Url.ToString();
            url += new string(' ', width - url.Length);

            return $"{url} {ResponseCode,5} {ContentType.Split(';').First(),15} {LoadTime,5:N0}ms {Links?.Count(),5:N0} links";
        }
    }
}
