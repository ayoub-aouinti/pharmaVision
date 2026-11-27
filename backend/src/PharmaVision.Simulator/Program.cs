using System.Globalization;
using PharmaVision.Simulator;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<SimulatorOptions>(builder.Configuration.GetSection("Simulator"));
// Raccourci « --speed 10 » ou « --speed 10x ».
if (builder.Configuration["speed"] is { } speed
    && double.TryParse(speed.TrimEnd('x'), CultureInfo.InvariantCulture, out var factor))
    builder.Services.PostConfigure<SimulatorOptions>(o => o.Speed = factor);
builder.Services.AddHostedService<Worker>();

builder.Build().Run();
