using System.Net;
using PicoDI;
using PicoWeb;
using PicoWeb.Samples;

var container = new SvcContainer();
container.Build();

await using var server = new WebServer(
    ShowcaseApp.Create(),
    new WebServerOptions { Endpoint = new IPEndPoint(IPAddress.Loopback, 7004) },
    container
);

await server.StartAsync();

Console.WriteLine($"PicoWeb showcase listening on {server.LocalEndPoint}");
Console.WriteLine("GET     /                    -> static landing page");
Console.WriteLine("GET     /api/showcase        -> sample capability summary");
Console.WriteLine("GET     /api/preferences     -> cookie-backed theme state");
Console.WriteLine("POST    /api/preferences/*   -> set theme cookie");
Console.WriteLine("GET     /api/content         -> compression demo payload");
Console.WriteLine("POST    /api/uploads         -> multipart form-data parsing");
Console.WriteLine("OPTIONS /api/*               -> CORS preflight");
Console.WriteLine("Press Enter to stop...");
Console.ReadLine();

await server.StopAsync();
