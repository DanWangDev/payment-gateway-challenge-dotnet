using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Resources;
using PaymentGateway.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Requests the framework cannot bind are reported with the same rejected shape as a failed rule, so
// merchants have one contract to handle. Validation still runs before the action.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<PaymentsController>>();
        // Binding errors can contain submitted values, so log only a fixed description.
        logger.LogInformation("Payment rejected: request body could not be bound");
        return new BadRequestObjectResult(new
        {
            status = PaymentStatus.Rejected,
            errors = new[] { PaymentRejectionMessages.BodyUnreadable }
        });
    };
});

builder.Services.AddSingleton<PaymentsRepository>();

var bankBaseUrl = builder.Configuration["BankSimulator:BaseUrl"]
    ?? throw new InvalidOperationException("BankSimulator:BaseUrl is not configured.");

if (!Uri.TryCreate(bankBaseUrl, UriKind.Absolute, out var bankUri)
    || (bankUri.Scheme != Uri.UriSchemeHttp && bankUri.Scheme != Uri.UriSchemeHttps))
{
    throw new InvalidOperationException(
        $"BankSimulator:BaseUrl must be an absolute HTTP(S) URL, but was '{bankBaseUrl}'.");
}

builder.Services.AddHttpClient<BankClient>(client =>
{
    client.BaseAddress = bankUri;
    // A bank silent for 10s is unavailable; the 100s default would leave the merchant waiting.
    client.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
