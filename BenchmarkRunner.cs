using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace VietnameseSpellChecker;

public static class BenchmarkRunner
{
    public static void RunAll(VietnameseAccentRestorer restorer)
    {
        Console.WriteLine("=========================================================================================");
        Console.WriteLine("             ĐÁNH GIÁ HIỆU NĂNG VÀ ĐỘ CHÍNH XÁC (C# .NET 10 BENCHMARK)                   ");
        Console.WriteLine("=========================================================================================");

        var testSuites = new (string Category, (string Query, string Expected)[] Cases)[]
        {
            (
                "1. Khôi phục Dấu Cơ Bản",
                [
                    ("dai hoc bach khoa ha noi", "đại học bách khoa hà nội"),
                    ("thoi tiet hom nay the nao", "thời tiết hôm nay thế nào"),
                    ("hoc sinh gioi thanh pho ho chi minh", "học sinh giỏi thành phố hồ chí minh"),
                    ("cong nghe thong tin va tri tue nhan tao", "công nghệ thông tin và trí tuệ nhân tạo"),
                    ("chuc mung nam moi an khang thinh vuong", "chúc mừng năm mới an khang thịnh vượng"),
                    ("chuyen tien tai khoan ngan hang", "chuyển tiền tài khoản ngân hàng"),
                    ("kinh te quoc dan ha noi", "kinh tế quốc dân hà nội")
                ]
            ),
            (
                "2. Sửa Lỗi Gõ Phím Telex",
                [
                    ("tooi muon ddi hocj", "tôi muốn đi học"),
                    ("ddang lam vieejc", "đang làm việc"),
                    ("nguwoif tieengs vietj", "người tiếng việt")
                ]
            ),
            (
                "3. Sửa Lỗi Ngữ Âm (s/x, tr/ch)",
                [
                    ("chia xe kinh nghiem", "chia sẻ kinh nghiệm"),
                    ("cau truyen co tich", "câu chuyện cổ tích"),
                    ("xap xep thoi gian", "sắp xếp thời gian")
                ]
            ),
            (
                "4. Sửa Lỗi Hỏi / Ngã",
                [
                    ("uống sửa tươi", "uống sữa tươi"),
                    ("sữa xe máy ha noi", "sửa xe máy hà nội")
                ]
            ),
            (
                "5. Giữ Nguyên Casing, Số, Ký tự Đặc biệt & Từ viết tắt (abbreviations.txt)",
                [
                    ("Hoc sinh cap 3, lop 12!", "Học sinh cấp 3, lớp 12!"),
                    ("hôm nay toi di hoc", "hôm nay tôi đi học"),
                    ("mua dien thoai iPhone", "mua điện thoại iPhone"),
                    ("ung dung AI va hoc IT", "ứng dụng AI và học IT"),
                    ("sinh song tai TPHCM va lam viec tai UBND", "sinh sống tại TPHCM và làm việc tại UBND")
                ]
            )
        };

        var allLatencies = new List<double>();
        int totalTests = 0;
        int passedTests = 0;

        foreach (var (category, cases) in testSuites)
        {
            Console.WriteLine($"\n--- {category} ---");
            Console.WriteLine($"{"STT",-4} | {"Query Đầu Vào",-35} | {"Kết Quả Trả Về",-35} | {"Thời Gian",-10} | Trạng Thái");
            Console.WriteLine(new string('-', 98));

            int idx = 1;
            foreach (var (query, expected) in cases)
            {
                totalTests++;
                var sw = Stopwatch.StartNew();
                string result = restorer.Restore(query);
                sw.Stop();

                double ms = sw.Elapsed.TotalMilliseconds;
                allLatencies.Add(ms);

                bool isCorrect = string.Equals(result, expected, StringComparison.OrdinalIgnoreCase);
                if (isCorrect)
                {
                    passedTests++;
                }

                string status = isCorrect ? "✓ ĐÚNG" : "✗ SAI";
                Console.WriteLine($"{idx++,-4} | {query,-35} | {result,-35} | {ms,7:F2} ms | {status}");
            }
        }

        Console.WriteLine("\n" + new string('=', 98));
        Console.WriteLine("                                  TỔNG KẾT BENCHMARK");
        Console.WriteLine(new string('=', 98));
        Console.WriteLine($"- Tổng số ca kiểm thử:    {totalTests}");
        Console.WriteLine($"- Số ca chính xác:        {passedTests} / {totalTests} ({passedTests * 100.0 / totalTests:F1}%)");
        Console.WriteLine($"- Độ trễ trung bình:      {allLatencies.Average():F2} ms / truy vấn");
        Console.WriteLine($"- Độ trễ nhanh nhất:      {allLatencies.Min():F2} ms");
        Console.WriteLine($"- Độ trễ chậm nhất:       {allLatencies.Max():F2} ms");
        Console.WriteLine(new string('=', 98));
    }
}
