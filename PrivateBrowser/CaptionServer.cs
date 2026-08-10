using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PrivateBrowser
{
    public class CaptionServer
    {
        private readonly CaptionStore _store;

        private WebApplication? _app;


        public CaptionServer(
            CaptionStore store
        )
        {
            _store = store;
        }


        public async Task StartAsync()
        {
            if (_app != null)
                return;


            WebApplicationBuilder builder =
                WebApplication.CreateBuilder();


            /*
             * IMPORTANT:
             *
             * Listen ONLY on 127.0.0.1.
             *
             * Do not use:
             *
             * 0.0.0.0
             *
             * because we don't want other machines
             * on the network accessing this endpoint.
             */

            builder.WebHost.UseUrls(
                "http://127.0.0.1:17832"
            );


            builder.Services.AddCors(
                options =>
                {
                    options.AddDefaultPolicy(
                        policy =>
                        {
                            policy
                                .AllowAnyOrigin()
                                .AllowAnyHeader()
                                .AllowAnyMethod();
                        }
                    );
                }
            );


            _app =
                builder.Build();


            _app.UseCors();


            // ---------------------------------------------
            // HEALTH CHECK
            // ---------------------------------------------

            _app.MapGet(
                "/health",
                () =>
                {
                    return Results.Json(
                        new
                        {
                            ok = true,
                            service =
                                "PrivateBrowser Caption Bridge"
                        }
                    );
                }
            );


            // ---------------------------------------------
            // RECEIVE CAPTION
            // ---------------------------------------------

            _app.MapPost(
                "/captions",
                async (
                    HttpContext context
                ) =>
                {
                    try
                    {
                        CaptionItem? caption =
                            await JsonSerializer
                                .DeserializeAsync<CaptionItem>(
                                    context.Request.Body,
                                    new JsonSerializerOptions
                                    {
                                        PropertyNameCaseInsensitive =
                                            true
                                    }
                                );


                        if (
                            caption == null ||
                            string.IsNullOrWhiteSpace(
                                caption.Text
                            )
                        )
                        {
                            return Results.BadRequest(
                                new
                                {
                                    ok = false,
                                    error =
                                        "Caption text is required."
                                }
                            );
                        }


                        if (
                            string.IsNullOrWhiteSpace(
                                caption.Timestamp
                            )
                        )
                        {
                            caption.Timestamp =
                                DateTime.UtcNow
                                    .ToString("O");
                        }


                        _store.Add(caption);


                        Console.WriteLine(
                            $"[CaptionServer] {caption.Speaker}: {caption.Text}"
                        );


                        return Results.Json(
                            new
                            {
                                ok = true
                            }
                        );
                    }
                    catch (Exception ex)
                    {
                        return Results.Json(
                            new
                            {
                                ok = false,
                                error = ex.Message
                            },
                            statusCode: 500
                        );
                    }
                }
            );


            // ---------------------------------------------
            // DEBUG: LATEST CAPTION
            // ---------------------------------------------

            _app.MapGet(
                "/captions/latest",
                () =>
                {
                    CaptionItem? latest =
                        _store.GetLatest();

                    if (latest == null)
                    {
                        return Results.NotFound(
                            new
                            {
                                ok = false
                            }
                        );
                    }


                    return Results.Json(latest);
                }
            );


            // ---------------------------------------------
            // DEBUG: ALL CAPTIONS
            // ---------------------------------------------

            _app.MapGet(
                "/captions",
                () =>
                {
                    return Results.Json(
                        _store.GetHistory()
                    );
                }
            );


            // ---------------------------------------------
            // CLEAR
            // ---------------------------------------------

            _app.MapDelete(
                "/captions",
                () =>
                {
                    _store.Clear();

                    return Results.Json(
                        new
                        {
                            ok = true
                        }
                    );
                }
            );


            await _app.StartAsync();
        }


        public async Task StopAsync()
        {
            if (_app == null)
                return;


            await _app.StopAsync();

            await _app.DisposeAsync();

            _app = null;
        }
    }
}