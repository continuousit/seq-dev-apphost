using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocoptNet;
using Seq.Api;
using Seq.Api.Model.Events;
using Seq.Apps;
using Seq.Apps.LogEvents;

namespace Seq.Dev.AppHost
{
    class Program
    {
        const string Usage = @"seq-dev-apphost: run a Seq app at the console.

Usage:
  seq-dev-apphost.exe <assembly> [--server=<s>] [--filter=<f>] [--apikey=<k>] [--window=<w>]
  seq-dev-apphost.exe (-h | --help)

Options:
  -h --help    Show this screen.
  <assembly>   Assembly from which to load the main reactor.
  --server=<s> Seq server URL [default: http://localhost:5341].
  --filter=<f> Filter expression or free text to match.
  --apikey=<k> Seq API key.
  --window=<w> Window size [default: 100].

    ";

        static void Main(string[] args)
        {
            var cts = new CancellationTokenSource();

            // ReSharper disable once MethodSupportsCancellation
            var tail = Task.Run(async () =>
            {
                try
                {
                    var arguments = new Docopt().Apply(Usage, args, version: "Seq Devevelopment App Host 0.1", exit: true);

                    var assembly = arguments["<assembly>"].ToString();
                    var server = arguments["--server"].ToString();
                    var apiKey = Normalize(arguments["--apikey"]);
                    var filter = Normalize(arguments["--filter"]);
                    var window = arguments["--window"].AsInt;
                    var assemblyName = assembly;

                    var searchPaths = new List<string>();
                    if (File.Exists(assembly))
                    {
                        assemblyName = Path.GetFileNameWithoutExtension(assemblyName);
                        var assemblyDir = Path.GetDirectoryName(assembly);
                        if (assemblyDir != null)
                            searchPaths.Add(assemblyDir);
                    }
                    searchPaths.Add(Environment.CurrentDirectory);
                    AssemblyResolver.Install(searchPaths.ToArray());

                    await Run(assemblyName, server, apiKey, filter, window, cts.Token);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.BackgroundColor = ConsoleColor.Red;
                    Console.WriteLine("seq-dev-apphost: {0}", ex);
                    Console.ResetColor();
                    Environment.Exit(-1);
                }
            });

            Console.ReadKey(true);
            cts.Cancel();
            // ReSharper disable once MethodSupportsCancellation
            tail.Wait();
        }

        static string Normalize(ValueObject v)
        {
            if (v == null) return null;
            var s = v.ToString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        static async Task Run(string assemblyName, string server, string apiKey, string filter, int window, CancellationToken cancel)
        {
            var app = LoadApp(assemblyName);
            var invoke = new Action<EventEntity>(e =>
            {
                var data = ConvertLogEventData(e);
                Send(app, data);
            });

            var connection = new SeqConnection(server, apiKey);

            await Tail(filter, window, cancel, connection, invoke);
        }

        static async Task Tail(string filter, int window, CancellationToken cancel, SeqConnection connection, Action<EventEntity> invoke)
        {
            var startedAt = DateTime.UtcNow;

            string strict = null;
            if (filter != null)
            {
                var converted = await connection.Expressions.ToStrictAsync(filter);
                strict = converted.StrictExpression;
            }

            var result = await connection.Events.ListAsync(count: window, render: true, fromDateUtc: startedAt, filter: strict);

            // Since results may come late, we request an overlapping window and exclude
            // events that have already been printed. If the last seen ID wasn't returned
            // we assume the count was too small to cover the window.
            var lastPrintedBatch = new HashSet<string>();
            string lastReturnedId = null;

            while (!cancel.IsCancellationRequested)
            {
                if (result.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
                else
                {
                    var noOverlap = result.All(e => e.Id != lastReturnedId);

                    if (noOverlap && lastReturnedId != null)
                        Console.WriteLine("<window exceeded>");

                    foreach (var eventEntity in ((IEnumerable<EventEntity>) result).Reverse())
                    {
                        if (lastPrintedBatch.Contains(eventEntity.Id))
                        {
                            continue;
                        }

                        lastReturnedId = eventEntity.Id;

                        var exception = "";
                        if (eventEntity.Exception != null)
                            exception = Environment.NewLine + eventEntity.Exception;

                        var ts = DateTimeOffset.Parse(eventEntity.Timestamp).ToLocalTime();

                        var color = ConsoleColor.White;
                        switch (eventEntity.Level)
                        {
                            case "Verbose":
                            case "Debug":
                                color = ConsoleColor.Gray;
                                break;
                            case "Warning":
                                color = ConsoleColor.Yellow;
                                break;
                            case "Error":
                            case "Fatal":
                                color = ConsoleColor.Red;
                                break;
                        }

                        Console.ForegroundColor = color;
                        Console.WriteLine("{0:G} [{1}] {2}{3}", ts, eventEntity.Level, eventEntity.RenderedMessage, exception);
                        Console.ResetColor();

                        invoke(eventEntity);
                    }

                    lastPrintedBatch = new HashSet<string>(result.Select(e => e.Id));
                }

                var fromDateUtc = lastReturnedId == null ? startedAt : DateTime.UtcNow.AddMinutes(-3);
                result = await connection.Events.ListAsync(count: window, render: true, fromDateUtc: fromDateUtc, filter: strict);
            }
        }

        static void Send(Reactor app, Event<LogEventData> data)
        {
        }

        static Event<LogEventData> ConvertLogEventData(EventEntity eventEntity)
        {
            throw new NotImplementedException();
        }

        static Reactor LoadApp(string assemblyName)
        {
            throw new NotImplementedException();
        }
    }
}
