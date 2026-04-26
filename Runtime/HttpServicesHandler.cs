using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fantasy;
using Fantasy.Async;
using Fantasy.Event;
using Fantasy.Network.HTTP;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.IdentityModel.Tokens;

namespace Entities.Http.Rpc;

/// <summary>
/// Registers the ASP.NET Core services required to expose Fantasy message handling over HTTP.
/// </summary>
public sealed class HttpServicesHandler : AsyncEventSystem<OnConfigureHttpServices>
{
    protected override async FTask Handler(OnConfigureHttpServices self)
    {
        var httpRpcOptions = BindOptions(self.Builder);
        self.Builder.Services.AddSingleton(httpRpcOptions);
        self.Builder.Services.AddSingleton<HttpProtoReflectionBridge>();
        self.Builder.Services.AddSingleton<HttpProtoSessionRegistry>();
        self.Builder.Services.AddSingleton<HttpProtoMessageDispatcher>();
        self.Builder.Services.AddSingleton<HttpJsonMessageDispatcher>();
        self.Builder.Services.AddSingleton<HttpMessagePackMessageDispatcher>();
        self.Builder.Services.AddSingleton<HttpMemoryPackMessageDispatcher>();
        self.Builder.Services.AddSingleton<HttpRpcPayloadProtector>();
        self.Builder.Services.AddHostedService<HttpProtoSessionCleanupService>();
        self.Builder.Services.AddOptions<HttpRpcOptions>()
            .Bind(self.Builder.Configuration.GetSection(HttpRpcOptions.SectionName))
            .Validate(configuration => HttpRpcOptionsValidator.Validate(configuration).Count == 0,
                $"Invalid {HttpRpcOptions.SectionName} configuration.")
            .ValidateOnStart();

        self.MvcBuilder.AddJsonOptions(mvcOptions =>
        {
            mvcOptions.JsonSerializerOptions.PropertyNamingPolicy = ConfigureNamingPolicy(httpRpcOptions.Json);
            mvcOptions.JsonSerializerOptions.WriteIndented = httpRpcOptions.Json.WriteIndented;
            mvcOptions.JsonSerializerOptions.DefaultIgnoreCondition = httpRpcOptions.Json.IgnoreNullValues
                ? JsonIgnoreCondition.WhenWritingNull
                : JsonIgnoreCondition.Never;

            if (httpRpcOptions.Json.SerializeEnumsAsStrings)
            {
                mvcOptions.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            }
        });

        self.MvcBuilder.AddMvcOptions(options =>
        {
        });

        self.Builder.Services.Configure<JsonOptions>(jsonOptions =>
        {
            jsonOptions.SerializerOptions.PropertyNamingPolicy = ConfigureNamingPolicy(httpRpcOptions.Json);
            jsonOptions.SerializerOptions.WriteIndented = httpRpcOptions.Json.WriteIndented;
            jsonOptions.SerializerOptions.DefaultIgnoreCondition = httpRpcOptions.Json.IgnoreNullValues
                ? JsonIgnoreCondition.WhenWritingNull
                : JsonIgnoreCondition.Never;

            if (httpRpcOptions.Json.SerializeEnumsAsStrings)
            {
                jsonOptions.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            }
        });

        self.Builder.Services.AddProblemDetails(problemDetailsOptions =>
        {
            problemDetailsOptions.CustomizeProblemDetails = context =>
            {
                // The trace identifier is generated inside the HTTP pipeline and is used to correlate
                // exception responses with the request log entry emitted by <see cref="HttpApplicationHandler"/>.
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

                if (httpRpcOptions.ErrorHandling.IncludeExceptionDetails)
                {
                    context.ProblemDetails.Extensions["sceneConfigId"] = self.Scene.SceneConfigId;
                }
            };
        });

        self.Builder.Services.AddExceptionHandler<HttpRpcExceptionHandler>();

        if (httpRpcOptions.Cors.Enabled)
        {
            self.Builder.Services.AddCors(corsOptions =>
            {
                corsOptions.AddPolicy(HttpRpcOptions.CorsPolicyName, builder =>
                {
                    builder.WithOrigins(httpRpcOptions.Cors.AllowedOrigins);

                    if (httpRpcOptions.Cors.AllowAnyMethod)
                    {
                        builder.AllowAnyMethod();
                    }
                    else
                    {
                        builder.WithMethods(httpRpcOptions.Cors.AllowedMethods);
                    }

                    if (httpRpcOptions.Cors.AllowAnyHeader)
                    {
                        builder.AllowAnyHeader();
                    }
                    else
                    {
                        builder.WithHeaders(httpRpcOptions.Cors.AllowedHeaders);
                    }

                    if (httpRpcOptions.Cors.AllowCredentials)
                    {
                        builder.AllowCredentials();
                    }
                });
            });
        }

        if (httpRpcOptions.Auth.Enabled)
        {
            self.Builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(jwtOptions =>
                {
                    jwtOptions.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = httpRpcOptions.Auth.ValidateIssuer,
                        ValidateAudience = httpRpcOptions.Auth.ValidateAudience,
                        ValidateLifetime = httpRpcOptions.Auth.ValidateLifetime,
                        ValidateIssuerSigningKey = httpRpcOptions.Auth.ValidateIssuerSigningKey,
                        ValidIssuer = httpRpcOptions.Auth.Issuer,
                        ValidAudience = httpRpcOptions.Auth.Audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(httpRpcOptions.Auth.SigningKey!)),
                        ClockSkew = TimeSpan.FromSeconds(httpRpcOptions.Auth.ClockSkewSeconds)
                    };
                });

            self.Builder.Services.AddAuthorizationBuilder()
                .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build());
        }

        if (httpRpcOptions.HealthChecks.Enabled)
        {
            self.Builder.Services.AddHealthChecks();
        }

        if (httpRpcOptions.ForwardedHeaders.Enabled)
        {
            self.Builder.Services.Configure<ForwardedHeadersOptions>(forwardedHeaders =>
            {
                forwardedHeaders.ForwardedHeaders = ResolveForwardedHeaders(httpRpcOptions.ForwardedHeaders);

                foreach (var proxy in httpRpcOptions.ForwardedHeaders.KnownProxies)
                {
                    forwardedHeaders.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
                }

                foreach (var network in httpRpcOptions.ForwardedHeaders.KnownNetworks)
                {
                    AddKnownNetwork(forwardedHeaders, network);
                }
            });
        }

        Log.Info($"[HTTP] HTTP RPC services configured: Scene {self.Scene.SceneConfigId}, AuthEnabled={httpRpcOptions.Auth.Enabled}, CorsEnabled={httpRpcOptions.Cors.Enabled}");

        await FTask.CompletedTask;
    }

    private static HttpRpcOptions BindOptions(WebApplicationBuilder builder)
    {
        // Bind once and validate eagerly so later registrations can safely depend on a fully-formed object.
        var options = new HttpRpcOptions();
        builder.Configuration.GetSection(HttpRpcOptions.SectionName).Bind(options);
        HttpRpcOptionsValidator.ValidateOrThrow(options);
        return options;
    }

    private static JsonNamingPolicy? ConfigureNamingPolicy(HttpRpcJsonOptions options)
    {
        return options.UseCamelCase ? JsonNamingPolicy.CamelCase : null;
    }

    internal static MessagePackSerializerOptions ConfigureMessagePackOptions(HttpRpcMessagePackOptions options)
    {
        var serializerOptions = options.UseContractlessResolver
            ? ContractlessStandardResolver.Options
            : StandardResolver.Options;

        if (options.UseLz4BlockArrayCompression)
        {
            serializerOptions = serializerOptions.WithCompression(MessagePackCompression.Lz4BlockArray);
        }

        return serializerOptions;
    }

    private static ForwardedHeaders ResolveForwardedHeaders(HttpRpcForwardedHeadersOptions options)
    {
        var headers = ForwardedHeaders.None;

        if (options.ForwardXForwardedFor)
        {
            headers |= ForwardedHeaders.XForwardedFor;
        }

        if (options.ForwardXForwardedProto)
        {
            headers |= ForwardedHeaders.XForwardedProto;
        }

        return headers;
    }

    private static void AddKnownNetwork(ForwardedHeadersOptions options, string cidr)
    {
#if NET10_0_OR_GREATER
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
#else
        var parts = cidr.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        options.KnownNetworks.Add(new IPNetwork(System.Net.IPAddress.Parse(parts[0]), int.Parse(parts[1])));
#endif
    }
}
