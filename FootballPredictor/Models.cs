using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FootballPredictor
{
    public sealed class Fixture
    {
        public string EventId { get; init; } = "";
        public string? HomeId { get; init; }
        public string? AwayId { get; init; }
        public string HomeName { get; init; } = "";
        public string AwayName { get; init; } = "";
        public string DateLocal { get; init; } = "";
    }

    public sealed class H2HStat
    {
        public int HomeWins { get; }
        public int AwayWins { get; }
        public int Draws { get; }
        public H2HStat(int hw, int aw, int dr) { HomeWins = hw; AwayWins = aw; Draws = dr; }
        public double HomeWinRate => (HomeWins + AwayWins + Draws) == 0 ? 0 :
                                     (double)HomeWins / (HomeWins + AwayWins + Draws);
        public double AwayWinRate => (HomeWins + AwayWins + Draws) == 0 ? 0 :
                                     (double)AwayWins / (HomeWins + AwayWins + Draws);
    }

    public sealed class XgModel
    {
        public double HomeXg { get; }
        public double AwayXg { get; }
        public XgModel(double home, double away) { HomeXg = home; AwayXg = away; }
    }

    public sealed class MatchView
    {
        public string Title { get; init; } = "";
        public string HomeName { get; init; } = "";
        public string AwayName { get; init; } = "";
        public List<string> HomeForm { get; init; } = new();
        public List<string> AwayForm { get; init; } = new();
    }
}
