using CommandLine;
using LibGit2Sharp;
using Rynco.Rikki.Db;
using Rynco.Rikki.GitOperator;

var options = Parser.Default.ParseArguments<Options>(args).Value;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<RikkiDbContext>();
builder.Services.AddScoped<HighDb>();
builder.Services.AddLogging();

var app = builder.Build();

app.UseRouting();
app.UsePathBase("/api/v1");
app.MapControllers();

app.Run();


class Options
{
    [Option("port", Default = 5000)]
    public required int Port { get; set; }

    [Option("host", Default = "localhost")]
    public required string Host { get; set; }

    [Option("git-root", Default = "./data/git")]
    public required string GitRoot { get; set; }
}
