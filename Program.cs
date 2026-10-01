using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace VietnameseSpellChecker;

class Program
{
    static void Main(string[] args)
    {
        // Cấu hình UTF-8 cho Console trên Windows
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        // Xử lý các cờ dòng lệnh offline nếu người dùng truyền vào
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].ToLower();
            if (arg is "--benchmark" or "-b" or "benchmark" or "test")
            {
                RunBenchmarkCli();
                return;
            }
            if (arg is "--interactive" or "-i")
            {
                RunInteractiveCli();
                return;
            }
            if (arg is "--query" or "-q" && i + 1 < args.Length)
            {
                RunSingleQueryCli(args[i + 1]);
                return;
            }
        }

        // Mặc định: Khởi động Web API & Web UI Testing Playground
        StartWebApiServer(args);
    }

    private static void StartWebApiServer(string[] args)
    {
        Console.WriteLine("=========================================================================================");
        Console.WriteLine("  KHỞI TẠO DỊCH VỤ WEB API & BỘ TEST SỬA LỖI CHÍNH TẢ TIẾNG VIỆT (C# .NET 10)");
        Console.WriteLine("=========================================================================================");

        var sw = Stopwatch.StartNew();
        Console.Write("Đang nạp Mô hình Ngôn ngữ N-gram & 18,698 từ viết tắt vào RAM... ");
        var lm = new NgramLanguageModel();
        var restorer = new VietnameseAccentRestorer(lm);
        sw.Stop();
        Console.WriteLine($"Hoàn tất trong {sw.ElapsedMilliseconds} ms!\n");

        var builder = WebApplication.CreateBuilder(args);

        // Đăng ký dịch vụ CORS và Restorer dạng Singleton
        builder.Services.AddCors();
        builder.Services.AddSingleton(restorer);

        // Cấu hình cổng lắng nghe (Mặc định: http://localhost:5000)
        builder.WebHost.UseUrls("http://localhost:5000");

        var app = builder.Build();

        app.UseCors(policy => policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());

        // 1. Endpoint Trang chủ Web UI Testing Playground
        app.MapGet("/", () => Results.Content(GetHtmlPlayground(), "text/html", Encoding.UTF8));

        // 2. Endpoint Health Check
        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = "online",
            framework = ".NET 10 / C# 13",
            totalUnigrams = lm.TotalUnigrams,
            timestamp = DateTime.UtcNow
        }));

        // 3. Endpoint Khôi phục Dấu & Sửa Lỗi (POST)
        app.MapPost("/api/restore", (RestoreRequest req, VietnameseAccentRestorer res) =>
        {
            if (string.IsNullOrWhiteSpace(req.Query))
            {
                return Results.BadRequest(new { error = "Query không được để trống." });
            }

            var timer = Stopwatch.StartNew();
            string result = res.Restore(req.Query, beamWidth: req.BeamWidth ?? 12);
            timer.Stop();

            return Results.Ok(new RestoreResponse(
                Query: req.Query,
                Result: result,
                LatencyMs: Math.Round(timer.Elapsed.TotalMilliseconds, 2)
            ));
        });

        // 4. Endpoint Khôi phục Dấu & Sửa Lỗi (GET - tiện test trực tiếp trên thanh địa chỉ duyệt web)
        app.MapGet("/api/restore", (string? q, int? beamWidth, VietnameseAccentRestorer res) =>
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.BadRequest(new { error = "Vui lòng truyền tham số ?q=..." });
            }

            var timer = Stopwatch.StartNew();
            string result = res.Restore(q, beamWidth: beamWidth ?? 12);
            timer.Stop();

            return Results.Ok(new RestoreResponse(
                Query: q,
                Result: result,
                LatencyMs: Math.Round(timer.Elapsed.TotalMilliseconds, 2)
            ));
        });

        // 5. Endpoint Gợi ý Top K Ứng viên (POST & GET)
        app.MapPost("/api/suggest", (SuggestRequest req, VietnameseAccentRestorer res) =>
        {
            if (string.IsNullOrWhiteSpace(req.Query))
            {
                return Results.BadRequest(new { error = "Query không được để trống." });
            }

            var timer = Stopwatch.StartNew();
            var suggestions = res.Suggest(req.Query, topK: req.TopK ?? 5, beamWidth: req.BeamWidth ?? 12);
            timer.Stop();

            return Results.Ok(new
            {
                query = req.Query,
                suggestions = suggestions.Select(s => new { text = s.Text, score = s.Score }),
                latencyMs = Math.Round(timer.Elapsed.TotalMilliseconds, 2)
            });
        });

        app.MapGet("/api/suggest", (string? q, int? topK, int? beamWidth, VietnameseAccentRestorer res) =>
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.BadRequest(new { error = "Vui lòng truyền tham số ?q=..." });
            }

            var timer = Stopwatch.StartNew();
            var suggestions = res.Suggest(q, topK: topK ?? 5, beamWidth: beamWidth ?? 12);
            timer.Stop();

            return Results.Ok(new
            {
                query = q,
                suggestions = suggestions.Select(s => new { text = s.Text, score = s.Score }),
                latencyMs = Math.Round(timer.Elapsed.TotalMilliseconds, 2)
            });
        });

        Console.WriteLine("=========================================================================================");
        Console.WriteLine("  DỊCH VỤ ĐÃ SẴN SÀNG!");
        Console.WriteLine("  - Giao diện Test trực quan:   http://localhost:5000");
        Console.WriteLine("  - API Sửa lỗi (Restore):      POST/GET http://localhost:5000/api/restore?q=...");
        Console.WriteLine("  - API Gợi ý (Suggest):        POST/GET http://localhost:5000/api/suggest?q=...");
        Console.WriteLine("  - API Kiểm tra (Health):      GET http://localhost:5000/api/health");
        Console.WriteLine("=========================================================================================\n");

        app.Run();
    }

    // Các DTO cho API
    public record RestoreRequest(string Query, int? BeamWidth = 12);
    public record RestoreResponse(string Query, string Result, double LatencyMs);
    public record SuggestRequest(string Query, int? TopK = 5, int? BeamWidth = 12);

    private static void RunBenchmarkCli()
    {
        using var lm = new NgramLanguageModel();
        var restorer = new VietnameseAccentRestorer(lm);
        BenchmarkRunner.RunAll(restorer);
    }

    private static void RunInteractiveCli()
    {
        using var lm = new NgramLanguageModel();
        var restorer = new VietnameseAccentRestorer(lm);
        Console.WriteLine("\n[REPL Mode] Gõ văn bản và nhấn Enter. Gõ 'exit' để thoát:");
        while (true)
        {
            Console.Write("\nQuery > ");
            string? line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

            var sw = Stopwatch.StartNew();
            string res = restorer.Restore(line);
            sw.Stop();
            Console.WriteLine($"-> {res} ({sw.Elapsed.TotalMilliseconds:F2} ms)");
        }
    }

    private static void RunSingleQueryCli(string query)
    {
        using var lm = new NgramLanguageModel();
        var restorer = new VietnameseAccentRestorer(lm);
        var sw = Stopwatch.StartNew();
        string res = restorer.Restore(query);
        sw.Stop();
        Console.WriteLine($"Query:  {query}");
        Console.WriteLine($"Result: {res} ({sw.Elapsed.TotalMilliseconds:F2} ms)");
    }

    private static string GetHtmlPlayground()
    {
        string indexPath = Path.Combine(AppContext.BaseDirectory, "index.html");
        if (File.Exists(indexPath))
        {
            return File.ReadAllText(indexPath, Encoding.UTF8);
        }
        if (File.Exists("index.html"))
        {
            return File.ReadAllText("index.html", Encoding.UTF8);
        }
        return "<h1>Vietnamese Spell Checker API is running!</h1><p>Visit /api/restore?q=... or /api/health</p>";
    }
}
