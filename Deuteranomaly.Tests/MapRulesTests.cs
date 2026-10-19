using Deuteranomaly.Core;
using Xunit;

namespace Deuteranomaly.Tests
{
    public class MapRulesTests
    {
        private const string LevelId = "custom_level_ABC123";

        [Fact]
        public void KeyStripsCustomLevelPrefix()
        {
            Assert.Equal("ABC123/Standard/ExpertPlus", MapRules.Key(LevelId, "Standard", "ExpertPlus"));
            Assert.Equal("100Bills/Standard/Expert", MapRules.Key("100Bills", "Standard", "Expert"));
        }

        [Fact]
        public void NoEntriesIsDefault()
        {
            Assert.Equal(MapRule.Default, MapRules.Find(null, new string[0], LevelId, "Standard", "Expert"));
        }

        [Fact]
        public void DifficultyEntryMatchesOnlyThatDifficulty()
        {
            var pins = new[] { "abc123/standard/expertplus" };
            Assert.Equal(MapRule.Pinned, MapRules.Find(pins, null, LevelId, "Standard", "ExpertPlus"));
            Assert.Equal(MapRule.Default, MapRules.Find(pins, null, LevelId, "Standard", "Expert"));
            Assert.Equal(MapRule.Default, MapRules.Find(pins, null, LevelId, "OneSaber", "ExpertPlus"));
        }

        [Fact]
        public void LevelEntryMatchesEveryDifficulty()
        {
            var exclusions = new[] { " ABC123 " };
            Assert.Equal(MapRule.Excluded, MapRules.Find(null, exclusions, LevelId, "Standard", "Easy"));
            Assert.Equal(MapRule.Excluded, MapRules.Find(null, exclusions, LevelId, "Lawless", "ExpertPlus"));
            Assert.Equal(MapRule.Default, MapRules.Find(null, exclusions, "custom_level_OTHER", "Standard", "Easy"));
        }

        [Fact]
        public void ExclusionWinsOverPin()
        {
            Assert.Equal(MapRule.Excluded, MapRules.Find(new[] { "ABC123" }, new[] { "ABC123/Standard/Hard" }, LevelId, "Standard", "Hard"));
        }

        [Fact]
        public void PinnedSettingsRaiseThresholdWithoutChangingTheOriginal()
        {
            var settings = new Settings { Threshold = 15 };
            Settings pinned = MapRules.ForPinned(settings, 40, 10);
            Assert.Equal(50, pinned.Threshold, 6);
            Assert.Equal(15, settings.Threshold, 6);

            Assert.Equal(15, MapRules.ForPinned(settings, 2, 10).Threshold, 6);
        }

        [Fact]
        public void PinnedMapChangesDistinguishableColors()
        {
            var settings = new Settings { Type = CvdType.Deutan, Severity = 0.6, Threshold = 15 };
            var red = new Rgb(0xC8 / 255.0, 0x14 / 255.0, 0x14 / 255.0);
            var blue = new Rgb(0x28 / 255.0, 0x8E / 255.0, 0xD2 / 255.0);
            Assert.False(ColorAssist.Resolve(red, blue, settings).Changed);

            double distance = ColorAssist.SimulatedDistance(red, blue, settings.Type, settings.Severity);
            Result pinned = ColorAssist.Resolve(red, blue, MapRules.ForPinned(settings, distance, 10));
            Assert.True(pinned.Changed);
            Assert.True(pinned.FinalDistance > distance);
        }
    }
}
