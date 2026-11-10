using CsRaiders.Api.API.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddCsRaidersApi();

var app = builder.Build();

await app.UseCsRaidersApi();

app.Run();
