using CodexUsageWidget.App.Models;
using CodexUsageWidget.App.Services;
using System.Text.Json;

var failures = new List<string>();

void Check(bool condition, string name)
{
    if (condition) Console.WriteLine($"PASS  {name}");
    else
    {
        Console.WriteLine($"FAIL  {name}");
        failures.Add(name);
    }
}

var sample = """
{"timestamp":"2030-01-01T00:00:00.000Z","payload":{"rate_limits":{"primary":{"used_percent":20.0,"window_minutes":300,"resets_at":1893474000},"secondary":{"used_percent":40.0,"window_minutes":10080,"resets_at":1894060800}}}}
""";
var parsed = LocalSessionUsageProvider.TryParseSessionLine(sample);
Check(parsed is not null, "JSONLレコードを解析できる");
Check(parsed?.FiveHour?.RemainingPercent == 80, "5時間の残り率を計算できる");
Check(parsed?.Weekly?.RemainingPercent == 60, "週間の残り率を計算できる");
Check(parsed?.FiveHour?.WindowMinutes == 300, "5時間枠を期間から識別できる");
Check(parsed?.Weekly?.WindowMinutes == 10080, "週間枠を期間から識別できる");

var clamped = new RateLimitWindow(125, 300, null);
Check(clamped.RemainingPercent == 0, "異常な使用率を安全に丸める");

var weeklyReset = new DateTimeOffset(2030, 1, 8, 0, 0, 0, TimeSpan.Zero);
var paceSample = new RateLimitWindow(24, 10080, weeklyReset);
var projectedPace = paceSample.ProjectedConsumptionPercent(new DateTimeOffset(2030, 1, 3, 0, 0, 0, TimeSpan.Zero));
Check(projectedPace is not null && Math.Abs(projectedPace.Value - 84) < 0.001, "週間の消費ペースを予測できる");
Check(new RateLimitWindow(24, 0, weeklyReset).ProjectedConsumptionPercent(weeklyReset) is null, "期間不明時は消費ペースを表示しない");

var renewalSettings = new WidgetSettings { SubscriptionRenewalAt = new DateTime(2030, 8, 1, 9, 30, 0) };
var restoredRenewalSettings = JsonSerializer.Deserialize<WidgetSettings>(JsonSerializer.Serialize(renewalSettings));
Check(restoredRenewalSettings?.SubscriptionRenewalAt == renewalSettings.SubscriptionRenewalAt, "手入力したサブスク更新日時を保存できる");

var january31 = new DateTime(2030, 1, 31, 9, 30, 0);
var februaryRenewal = SubscriptionRenewalSchedule.AdvanceToFuture(january31, null, new DateTime(2030, 2, 1, 0, 0, 0));
Check(februaryRenewal.NextRenewalAt == new DateTime(2030, 2, 28, 9, 30, 0) && februaryRenewal.AnchorDay == 31,
    "月末の更新日を翌月末へ繰り越せる");
var marchRenewal = SubscriptionRenewalSchedule.AdvanceToFuture(februaryRenewal.NextRenewalAt, februaryRenewal.AnchorDay, new DateTime(2030, 2, 28, 10, 0, 0));
Check(marchRenewal.NextRenewalAt == new DateTime(2030, 3, 31, 9, 30, 0),
    "月末の更新日を翌々月の元の日付へ戻せる");

var claudeSample = """
{"five_hour":{"utilization":37.0,"resets_at":"2030-01-01T05:00:00.000000+00:00"},"seven_day":{"utilization":12.5,"resets_at":"2030-01-07T00:00:00Z"},"seven_day_opus":null}
""";
var claude = ClaudeUsageProvider.ParseUsage(claudeSample, new DateTimeOffset(2030, 1, 1, 2, 0, 0, TimeSpan.Zero));
Check(claude?.FiveHour?.RemainingPercent == 63, "Claudeの5時間の残り率を計算できる");
Check(claude?.Weekly?.RemainingPercent == 87.5, "Claudeの週間の残り率を計算できる");
Check(claude?.FiveHour?.ResetsAt == new DateTimeOffset(2030, 1, 1, 5, 0, 0, TimeSpan.Zero), "Claudeのリセット時刻を解析できる");
Check(claude?.FiveHour?.ProjectedConsumptionPercent(claude.ObservedAt) is double claudePace && Math.Abs(claudePace - 92.5) < 0.001,
    "Claudeの5時間枠でもペースを予測できる");
Check(ClaudeUsageProvider.ParseUsage("""{"five_hour":null,"seven_day":null}""", DateTimeOffset.UtcNow) is null,
    "Claudeの利用枠がない応答はデータなしとして扱う");

var claudeWithModel = ClaudeUsageProvider.ParseUsage("""
{"five_hour":{"utilization":8.0,"resets_at":"2030-01-01T05:00:00+00:00"},"seven_day":{"utilization":10.0,"resets_at":"2030-01-07T00:00:00+00:00"},
 "limits":[{"kind":"weekly_all","percent":10,"resets_at":"2030-01-07T00:00:00+00:00","scope":null},
           {"kind":"weekly_scoped","percent":25,"resets_at":"2030-01-07T00:00:00+00:00","scope":{"model":{"id":null,"display_name":"Fable"},"surface":null}}]}
""", DateTimeOffset.UtcNow);
Check(claudeWithModel?.ModelLimit is { ModelName: "Fable", Window.RemainingPercent: 75, Window.WindowMinutes: 10080 },
    "Claudeのモデル別の週間枠（Fable）を解析できる");
Check(claude?.ModelLimit is null, "モデル別の枠がない応答では行を出さない");

var credential = ClaudeUsageProvider.ParseCredential("""{"claudeAiOauth":{"accessToken":"token","expiresAt":1893456000000}}""");
Check(credential?.AccessToken == "token" && credential.ExpiresAt == DateTimeOffset.FromUnixTimeMilliseconds(1893456000000),
    "Claude Codeのログイン情報を読み取れる");
Check(ClaudeUsageProvider.ParseCredential("""{"claudeAiOauth":{}}""") is null, "トークンがないログイン情報は無視する");

var monthlyReset = new DateTimeOffset(2030, 1, 31, 0, 0, 0, TimeSpan.Zero);
var freePlan = new UsageSnapshot(new RateLimitWindow(32, 43200, monthlyReset), null, DateTimeOffset.UtcNow, UsageSource.AppServer);
Check(freePlan.FiveHour is null && freePlan.Weekly?.WindowMinutes == 43200, "30日枠だけのプランは長期の行に置く");
Check(freePlan.Weekly?.NonStandardLabel == "30日枠" && parsed?.Weekly?.NonStandardLabel is null, "標準外の枠だけ長さを表示する");
Check(new RateLimitWindow(5, 43200, monthlyReset).ProjectedConsumptionPercent(monthlyReset.AddDays(-29.5)) is null,
    "期間の始まり直後はペースを出さない");

var desktopKey = new byte[32];
Random.Shared.NextBytes(desktopKey);
string EncryptLikeDesktop(string plain)
{
    var nonce = new byte[12];
    Random.Shared.NextBytes(nonce);
    var plainBytes = System.Text.Encoding.UTF8.GetBytes(plain);
    var cipher = new byte[plainBytes.Length];
    var tag = new byte[16];
    using var aes = new System.Security.Cryptography.AesGcm(desktopKey, 16);
    aes.Encrypt(nonce, plainBytes, cipher, tag);
    return Convert.ToBase64String([.. "v10"u8, .. nonce, .. cipher, .. tag]);
}
var desktopCache = """
{"inst:user:https://api.anthropic.com:user:inference":{"token":"inference-only","expiresAt":1893456000000},
 "inst:user:https://api.anthropic.com:user:inference user:profile":{"token":"full","expiresAt":1893400000000},
 "inst:user:https://api.anthropic.com:user:inference_extra user:profile":{"token":"lookalike","expiresAt":1893500000000}}
""";
var decrypted = ClaudeDesktopCredentialReader.DecryptValue(EncryptLikeDesktop(desktopCache), desktopKey);
Check(decrypted == desktopCache, "デスクトップアプリの暗号化キャッシュを復号できる");
Check(ClaudeDesktopCredentialReader.DecryptValue(EncryptLikeDesktop(desktopCache), new byte[32]) is null, "鍵が違えば復号しない");
var desktopToken = ClaudeDesktopCredentialReader.SelectToken(desktopCache);
Check(desktopToken?.AccessToken == "full" && desktopToken.ExpiresAt == DateTimeOffset.FromUnixTimeMilliseconds(1893400000000),
    "使用量APIに必要なスコープを持つトークンを優先する");

var displaySettings = new WidgetSettings();
displaySettings.Display.ShowClaude = false;
displaySettings.Display.ShowPace = false;
var restoredDisplay = JsonSerializer.Deserialize<WidgetSettings>(JsonSerializer.Serialize(displaySettings))?.Display;
Check(restoredDisplay is { ShowClaude: false, ShowPace: false, ShowCodex: true, ShowReset: true, ShowDetailLine: true },
    "表示項目の切り替えを保存できる");
Check(JsonSerializer.Deserialize<WidgetSettings>("""{"RefreshSeconds":30}""")?.Display is { ShowCodex: true, ShowClaude: true },
    "表示項目がない旧設定では全項目を表示する");

var perProvider = new WidgetSettings();
perProvider.SetRenewal(ProviderKind.Claude, new DateTime(2030, 5, 3, 9, 0, 0), 3);
Check(perProvider.GetRenewal(ProviderKind.Claude).At == new DateTime(2030, 5, 3, 9, 0, 0) &&
      perProvider.GetRenewal(ProviderKind.Codex).At is null,
    "サブスク更新日をサービスごとに保存できる");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} test(s) failed.");
    return 1;
}

Console.WriteLine("All tests passed.");
return 0;
