using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;

namespace FootballPredictor
{
    public partial class MainWindow : Window
    {
        private const string ApiKey = "123"; // V1 free
        private const string BaseUrl = "https://www.thesportsdb.com/api/v1/json";
        private const string LeagueId = "4328"; // EPL

        private readonly HttpClient _http = new HttpClient();

        public MainWindow() => InitializeComponent();

        private async void PredictButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PredictionText.Text = "Đang tải 3 trận EPL và form 5 trận gần nhất...";
                var fixtures = await GetNextLeagueFixturesAsync(LeagueId, 3);
                if (fixtures.Count == 0)
                {
                    MessageBox.Show("Chưa có lịch tương lai từ API.");
                    return;
                }

                // Tạo view và đảm bảo luôn có 5 chấm
                var views = new List<MatchView>();
                foreach (var fx in fixtures)
                {
                    // Fallback tra id theo tên khi thiếu
                    string? homeId = fx.HomeId;
                    string? awayId = fx.AwayId;
                    if (string.IsNullOrWhiteSpace(homeId))
                        homeId = await ResolveTeamIdByNameAsync(fx.HomeName);
                    if (string.IsNullOrWhiteSpace(awayId))
                        awayId = await ResolveTeamIdByNameAsync(fx.AwayName);

                    var homeForm = !string.IsNullOrWhiteSpace(homeId) ? await GetLastResultsAsync(homeId!, 5) : new List<string>();
                    var awayForm = !string.IsNullOrWhiteSpace(awayId) ? await GetLastResultsAsync(awayId!, 5) : new List<string>();

                    // Điền đủ 5 phần tử (nếu thiếu thì thêm 'D' trung tính để ItemsControl có item)
                    while (homeForm.Count < 5) homeForm.Add("D");
                    while (awayForm.Count < 5) awayForm.Add("D");

                    views.Add(new MatchView
                    {
                        Title = $"{fx.HomeName} - {fx.AwayName} | {fx.DateLocal}",
                        HomeName = fx.HomeName,
                        AwayName = fx.AwayName,
                        HomeForm = homeForm,
                        AwayForm = awayForm
                    });
                }
                MatchesListBox.ItemsSource = views;

                // Dự đoán: dùng 1 mùa gần nhất + form 3 trận cho nhẹ
                PredictionText.Text = "";
                var seasons = await GetLeagueSeasonsAsync(LeagueId);
                var seasonsToScan = seasons.OrderByDescending(s => s).Take(1).ToList();

                foreach (var fx in fixtures)
                {
                    var h2h = await ComputeHeadToHeadAsync(LeagueId, seasonsToScan, fx.HomeName, fx.AwayName);
                    var formHome = fx.HomeId != null ? await ComputeFormAsync(fx.HomeId!, 3) : 0.0;
                    var formAway = fx.AwayId != null ? await ComputeFormAsync(fx.AwayId!, 3) : 0.0;
                    var lineup = await GetLineupFactorAsync(fx.EventId);

                    var xg = BuildExpectedGoals(h2h, formHome, formAway, lineup);
                    var (hs, ascore, pH, pD, pA) = MostLikelyScore(xg.HomeXg, xg.AwayXg);

                    PredictionText.Text +=
                        $"{fx.HomeName} {hs}-{ascore} {fx.AwayName}  | P(H/D/A)≈ {pH:P0}/{pD:P0}/{pA:P0}\n";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi: {ex.Message}");
            }
        }

        /* ================= API helpers ================= */

        private async Task<dynamic> GetJsonAsync(string url)
        {
            var text = await _http.GetStringAsync(url);
            return JsonConvert.DeserializeObject(text) ?? new { };
        }

        private async Task<List<Fixture>> GetNextLeagueFixturesAsync(string leagueId, int take)
        {
            var url = $"{BaseUrl}/{ApiKey}/eventsnextleague.php?id={leagueId}";
            dynamic data = await GetJsonAsync(url);
            var list = new List<Fixture>();
            if (data?.events == null) return list;

            int count = 0;
            foreach (var ev in data.events)
            {
                if (count++ >= take) break;
                list.Add(new Fixture
                {
                    EventId = (string)(ev.idEvent ?? ""),
                    HomeId = ev.idHomeTeam != null ? (string)ev.idHomeTeam : null,
                    AwayId = ev.idAwayTeam != null ? (string)ev.idAwayTeam : null,
                    HomeName = (string)ev.strHomeTeam,
                    AwayName = (string)ev.strAwayTeam,
                    DateLocal = $"{ev.dateEvent} {ev.strTime}"
                });
            }
            return list;
        }

        private async Task<List<string>> GetLeagueSeasonsAsync(string leagueId)
        {
            var url = $"{BaseUrl}/{ApiKey}/search_all_seasons.php?id={leagueId}";
            dynamic data = await GetJsonAsync(url);
            var seasons = new List<string>();
            if (data?.seasons == null) return seasons;
            foreach (var s in data.seasons) seasons.Add((string)s.strSeason);
            return seasons;
        }

        private async Task<H2HStat> ComputeHeadToHeadAsync(string leagueId, List<string> seasons, string home, string away)
        {
            int hw = 0, aw = 0, dr = 0;
            foreach (var season in seasons)
            {
                var url = $"{BaseUrl}/{ApiKey}/eventsseason.php?id={leagueId}&s={season}";
                dynamic data = await GetJsonAsync(url);
                if (data?.events == null) continue;

                foreach (var ev in data.events)
                {
                    string h = (string)ev.strHomeTeam;
                    string a = (string)ev.strAwayTeam;
                    bool same = h.Equals(home, StringComparison.OrdinalIgnoreCase) && a.Equals(away, StringComparison.OrdinalIgnoreCase);
                    bool flipped = h.Equals(away, StringComparison.OrdinalIgnoreCase) && a.Equals(home, StringComparison.OrdinalIgnoreCase);
                    if (!same && !flipped) continue;

                    if (int.TryParse((string)(ev.intHomeScore ?? ""), out int hs) &&
                        int.TryParse((string)(ev.intAwayScore ?? ""), out int ascore))
                    {
                        if (same)
                        {
                            if (hs > ascore) hw++; else if (hs < ascore) aw++; else dr++;
                        }
                        else
                        {
                            if (ascore > hs) hw++; else if (ascore < hs) aw++; else dr++;
                        }
                    }
                }
            }
            return new H2HStat(hw, aw, dr);
        }

        private async Task<double> ComputeFormAsync(string teamId, int nGames)
        {
            var url = $"{BaseUrl}/{ApiKey}/eventslast.php?id={teamId}";
            dynamic data = await GetJsonAsync(url);
            if (data?.results == null) return 0.0;

            double score = 0.0; int n = 0;
            foreach (var ev in data.results)
            {
                if (n++ >= nGames) break;
                int hs = int.TryParse((string)(ev.intHomeScore ?? ""), out var hh) ? hh : 0;
                int ascore = int.TryParse((string)(ev.intAwayScore ?? ""), out var aa) ? aa : 0;
                string idH = ev.idHomeTeam != null ? (string)ev.idHomeTeam : "";
                bool isHome = idH == teamId;

                int pts = hs == ascore ? 1 : ((isHome && hs > ascore) || (!isHome && ascore > hs) ? 3 : 0);
                int gd = isHome ? (hs - ascore) : (ascore - hs);
                score += pts + 0.25 * gd;
            }
            return score / Math.Max(1, n);
        }

        private async Task<double> GetLineupFactorAsync(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return 0.0;
            var url = $"{BaseUrl}/{ApiKey}/lookuplineup.php?id={eventId}";
            dynamic data = await GetJsonAsync(url);
            if (data?.lineup == null) return 0.0;
            try
            {
                var home = data.lineup.home != null ? data.lineup.home : null;
                var away = data.lineup.away != null ? data.lineup.away : null;
                int hc = home?.startXI?.Count ?? 0;
                int ac = away?.startXI?.Count ?? 0;
                if (hc >= 11 && ac >= 11) return 1.0;
                if (hc >= 11 || ac >= 11) return 0.5;
                return 0.0;
            }
            catch { return 0.0; }
        }

        private XgModel BuildExpectedGoals(H2HStat h2h, double formHome, double formAway, double lineup)
        {
            double baseHome = 1.45, baseAway = 1.25;
            double formDelta = (formHome - formAway);
            double h2hDelta = (h2h.HomeWinRate - h2h.AwayWinRate);
            double lh = baseHome + 0.18 * formDelta + 0.12 * h2hDelta + 0.20 * lineup;
            double la = baseAway - 0.18 * formDelta - 0.12 * h2hDelta - 0.20 * lineup;
            lh = Math.Clamp(lh, 0.2, 3.5);
            la = Math.Clamp(la, 0.2, 3.5);
            return new XgModel(lh, la);
        }

        private static double Poi(int k, double l) => Math.Exp(-l) * Math.Pow(l, k) / Factorial(k);
        private static double Factorial(int n) { double r = 1; for (int i = 2; i <= n; i++) r *= i; return r; }

        private (int hs, int ascore, double pH, double pD, double pA) MostLikelyScore(double lh, double la)
        {
            double best = -1; int bh = 0, ba = 0;
            double pH = 0, pD = 0, pA = 0;
            var ph = Enumerable.Range(0, 7).Select(k => Poi(k, lh)).ToArray();
            var pa = Enumerable.Range(0, 7).Select(k => Poi(k, la)).ToArray();
            for (int h = 0; h <= 6; h++)
                for (int a = 0; a <= 6; a++)
                {
                    double p = ph[h] * pa[a];
                    if (h > a) pH += p; else if (h == a) pD += p; else pA += p;
                    if (p > best) { best = p; bh = h; ba = a; }
                }
            return (bh, ba, Math.Clamp(pH, 0, 1), Math.Clamp(pD, 0, 1), Math.Clamp(pA, 0, 1));
        }

        // === Fallback: tra id đội theo tên ===
        private async Task<string?> ResolveTeamIdByNameAsync(string teamName)
        {
            var url = $"{BaseUrl}/{ApiKey}/searchteams.php?t={Uri.EscapeDataString(teamName)}";
            dynamic data = await GetJsonAsync(url);
            if (data?.teams == null) return null;
            try { return (string)data.teams[0].idTeam; }
            catch { return null; }
        }

        // === Lấy W/D/L của N trận gần nhất ===
        private async Task<List<string>> GetLastResultsAsync(string teamId, int n)
        {
            var url = $"{BaseUrl}/{ApiKey}/eventslast.php?id={teamId}";
            dynamic data = await GetJsonAsync(url);
            var res = new List<string>();
            if (data?.results == null) return res;

            foreach (var ev in data.results)
            {
                if (res.Count >= n) break;
                int hs = int.TryParse((string)(ev.intHomeScore ?? ""), out var hh) ? hh : 0;
                int ascore = int.TryParse((string)(ev.intAwayScore ?? ""), out var aa) ? aa : 0;
                string idH = ev.idHomeTeam != null ? (string)ev.idHomeTeam : "";
                bool isHome = idH == teamId;

                if (hs == ascore) res.Add("D");
                else
                {
                    bool win = (isHome && hs > ascore) || (!isHome && ascore > hs);
                    res.Add(win ? "W" : "L");
                }
            }
            return res;
        }
    }
}
