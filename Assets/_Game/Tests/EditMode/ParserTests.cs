using NUnit.Framework;
using SmallTown.Commands;
using SmallTown.Simulation.Events;
using SmallTown.Simulation.World;

namespace SmallTown.Tests
{
    public sealed class ParserTests
    {
        private static CommandParser _parser;

        private static ParseContext Ctx()
        {
            return new ParseContext { City = TestUtil.City() };
        }

        private static ParseResult Parse(string text, ParseContext ctx = null)
        {
            if (_parser == null) _parser = new CommandParser(TestUtil.Grammar());
            return _parser.Parse(text, ctx ?? Ctx());
        }

        private static WorldCommand Single(string text, ParseContext ctx = null)
        {
            var r = Parse(text, ctx);
            Assert.AreEqual(ParseStatus.Ok, r.Status, text + " → " + r.Message);
            Assert.AreEqual(1, r.Commands.Count, text);
            Assert.AreEqual(SpecKind.World, r.Commands[0].Kind, text);
            return r.Commands[0].World;
        }

        private static EntityRef Place(string key) => new EntityRef(EntityKind.Place, TestUtil.City().PlaceByKey(key).Id);

        // ------------------------------------------------------------------ bridges

        [TestCase("закрой северный мост", CommandKind.CloseBridge, 0)]
        [TestCase("перекрой мост на севере", CommandKind.CloseBridge, 0)]
        [TestCase("Закройте, пожалуйста, северный мост!", CommandKind.CloseBridge, 0)]
        [TestCase("close the north bridge", CommandKind.CloseBridge, 0)]
        [TestCase("закрой южный мост", CommandKind.CloseBridge, 1)]
        [TestCase("block the southern bridge", CommandKind.CloseBridge, 1)]
        [TestCase("закрой оба моста", CommandKind.CloseBridge, 2)]
        [TestCase("close both bridges", CommandKind.CloseBridge, 2)]
        [TestCase("открой северный мост", CommandKind.OpenBridge, 0)]
        [TestCase("reopen the south bridge", CommandKind.OpenBridge, 1)]
        public void Bridges(string text, CommandKind kind, int which)
        {
            var w = Single(text);
            Assert.AreEqual(kind, w.Kind);
            Assert.AreEqual(which, w.IntArg);
        }

        [Test]
        public void AmbiguousBridge_AsksWhichOne()
        {
            var r = Parse("закрой мост");
            Assert.AreEqual(ParseStatus.Ambiguous, r.Status);
            Assert.AreEqual(3, r.Options.Count);
            var picked = Parse(r.Options[1].Text);
            Assert.AreEqual(CommandKind.CloseBridge, picked.Commands[0].World.Kind);
            Assert.AreEqual(1, picked.Commands[0].World.IntArg);
        }

        [Test]
        public void OpenBridge_WhenOnlyOneClosed_IsNotAmbiguous()
        {
            var ctx = Ctx();
            ctx.NorthClosed = true;
            var w = Single("открой мост", ctx);
            Assert.AreEqual(CommandKind.OpenBridge, w.Kind);
            Assert.AreEqual(0, w.IntArg);
        }

        [Test]
        public void It_RefersToLastChangedEntity()
        {
            var ctx = Ctx();
            ctx.Last = Place("north_bridge");
            var w = Single("открой его", ctx);
            Assert.AreEqual(CommandKind.OpenBridge, w.Kind);
            Assert.AreEqual(0, w.IntArg);
        }

        [Test]
        public void This_RefersToSelection()
        {
            var ctx = Ctx();
            ctx.Selected = Place("south_bridge");
            var w = Single("закрой это", ctx);
            Assert.AreEqual(CommandKind.CloseBridge, w.Kind);
            Assert.AreEqual(1, w.IntArg);
        }

        [Test]
        public void This_WithoutSelection_IsHonestlyRejected()
        {
            var r = Parse("подожги это");
            Assert.AreEqual(ParseStatus.CannotDo, r.Status);
            StringAssert.Contains("это", r.Message);
        }

        // ------------------------------------------------------------------ river

        [TestCase("подними реку на 50 см", 0.5f)]
        [TestCase("подними реку на полметра", 0.5f)]
        [TestCase("подними реку на 1 метр", 1f)]
        [TestCase("подними воду на метр", 1f)]
        [TestCase("подними уровень воды на 30 сантиметров", 0.3f)]
        [TestCase("подними реку на 1,5 метра", 1.5f)]
        [TestCase("подними реку на полтора метра", 1.5f)]
        [TestCase("подними реку на два метра", 2f)]
        [TestCase("raise the river by 1 meter", 1f)]
        [TestCase("raise the water by half a meter", 0.5f)]
        [TestCase("опусти реку на 30 сантиметров", -0.3f)]
        [TestCase("lower the river by 50cm", -0.5f)]
        [TestCase("затопи город", 1f)]
        public void River(string text, float metres)
        {
            var w = Single(text);
            Assert.AreEqual(CommandKind.RiverDelta, w.Kind);
            Assert.AreEqual(metres, w.FloatArg, 0.001f);
        }

        [TestCase("верни реку в норму")]
        [TestCase("вода в норму")]
        [TestCase("return the river to normal")]
        public void RiverNormal(string text)
        {
            Assert.AreEqual(CommandKind.RiverNormal, Single(text).Kind);
        }

        [Test]
        public void RiverWithoutAmount_AsksHowMuch()
        {
            var r = Parse("подними реку");
            Assert.AreEqual(ParseStatus.Ambiguous, r.Status);
            Assert.AreEqual(3, r.Options.Count);
            var picked = Parse(r.Options[0].Text);
            Assert.AreEqual(0.5f, picked.Commands[0].World.FloatArg, 0.001f);
        }

        [Test]
        public void RaiseIt_UsesLastEntity()
        {
            var ctx = Ctx();
            ctx.Last = Place("river");
            var w = Single("подними его на 50 см", ctx);
            Assert.AreEqual(CommandKind.RiverDelta, w.Kind);
            Assert.AreEqual(0.5f, w.FloatArg, 0.001f);
        }

        // ------------------------------------------------------------------ weather, time, seasons

        [TestCase("пусть пойдёт дождь", Weather.Rain)]
        [TestCase("включи дождь", Weather.Rain)]
        [TestCase("make it rain", Weather.Rain)]
        [TestCase("начни грозу", Weather.Storm)]
        [TestCase("thunderstorm please", Weather.Storm)]
        [TestCase("пусть идёт снег", Weather.Snow)]
        [TestCase("let it snow", Weather.Snow)]
        [TestCase("останови дождь", Weather.Clear)]
        [TestCase("make it sunny", Weather.Clear)]
        [TestCase("сделай ясную погоду", Weather.Clear)]
        public void WeatherCommands(string text, Weather expected)
        {
            var w = Single(text);
            Assert.AreEqual(CommandKind.SetWeather, w.Kind);
            Assert.AreEqual((int)expected, w.IntArg);
        }

        [TestCase("сделай ночь", 23f)]
        [TestCase("верни утро", 8f)]
        [TestCase("сделай рассвет", 6f)]
        [TestCase("закат", 19.5f)]
        [TestCase("sunset", 19.5f)]
        [TestCase("make it night", 23f)]
        [TestCase("сделай 18:30", 18.5f)]
        [TestCase("в 9 вечера", 21f)]
        [TestCase("at 7 am", 7f)]
        [TestCase("полдень", 12f)]
        public void TimeOfDay(string text, float hour)
        {
            var w = Single(text);
            Assert.AreEqual(CommandKind.SetTime, w.Kind);
            Assert.AreEqual(hour, w.FloatArg, 0.01f);
        }

        [TestCase("наступает зима", Season.Winter)]
        [TestCase("сделай осень", Season.Autumn)]
        [TestCase("make it autumn", Season.Autumn)]
        [TestCase("spring", Season.Spring)]
        [TestCase("пусть будет лето", Season.Summer)]
        public void Seasons(string text, Season s)
        {
            var w = Single(text);
            Assert.AreEqual(CommandKind.SetSeason, w.Kind);
            Assert.AreEqual((int)s, w.IntArg);
        }

        // ------------------------------------------------------------------ events

        [TestCase("начни фестиваль в центральном парке", CommandKind.StartFestival)]
        [TestCase("устрой праздник в парке", CommandKind.StartFestival)]
        [TestCase("start a festival", CommandKind.StartFestival)]
        [TestCase("закончи фестиваль", CommandKind.EndFestival)]
        [TestCase("отмени фестиваль", CommandKind.EndFestival)]
        [TestCase("end the festival", CommandKind.EndFestival)]
        [TestCase("преврати парковку в парк", CommandKind.LotToPark)]
        [TestCase("сделай из парковки парк", CommandKind.LotToPark)]
        [TestCase("turn the parking lot into a park", CommandKind.LotToPark)]
        [TestCase("верни парковку", CommandKind.ParkToLot)]
        [TestCase("turn the park back into parking", CommandKind.ParkToLot)]
        [TestCase("ограбь банк", CommandKind.Robbery)]
        [TestCase("rob the bank", CommandKind.Robbery)]
        [TestCase("потуши пожар", CommandKind.Extinguish)]
        [TestCase("put out the fire", CommandKind.Extinguish)]
        public void Events(string text, CommandKind kind)
        {
            Assert.AreEqual(kind, Single(text).Kind);
        }

        [TestCase("подожги школу")]
        [TestCase("пожар в школе")]
        [TestCase("set the school on fire")]
        public void FireInSchool(string text)
        {
            var w = Single(text);
            Assert.AreEqual(CommandKind.StartFire, w.Kind);
            Assert.AreEqual(TestUtil.City().SchoolBuilding, w.IntArg);
        }

        [Test]
        public void BurnThis_UsesSelectedBuilding()
        {
            var ctx = Ctx();
            ctx.Selected = Place("bank");
            var w = Single("подожги это", ctx);
            Assert.AreEqual(CommandKind.StartFire, w.Kind);
            Assert.AreEqual(TestUtil.City().BankBuilding, w.IntArg);
        }

        [Test]
        public void FireWithoutTarget_Asks()
        {
            var r = Parse("пожар!");
            Assert.AreEqual(ParseStatus.Ambiguous, r.Status);
            Assert.GreaterOrEqual(r.Options.Count, 2);
        }

        // ------------------------------------------------------------------ compound, session and unknown

        [Test]
        public void TwoCommandsJoinedByAnd()
        {
            var r = Parse("закрой северный мост и включи дождь");
            Assert.AreEqual(ParseStatus.Ok, r.Status);
            Assert.AreEqual(2, r.Commands.Count);
            Assert.AreEqual(CommandKind.CloseBridge, r.Commands[0].World.Kind);
            Assert.AreEqual(CommandKind.SetWeather, r.Commands[1].World.Kind);
            Assert.AreEqual((int)Weather.Rain, r.Commands[1].World.IntArg);
        }

        [Test]
        public void SharedVerbAcrossAnd()
        {
            var r = Parse("закрой северный и южный мост");
            Assert.AreEqual(2, r.Commands.Count);
            Assert.AreEqual(0, r.Commands[0].World.IntArg);
            Assert.AreEqual(1, r.Commands[1].World.IntArg);
            Assert.AreEqual(CommandKind.CloseBridge, r.Commands[1].World.Kind);
        }

        [Test]
        public void EnglishAndCompound()
        {
            var r = Parse("close the south bridge and raise the river by 50 cm");
            Assert.AreEqual(2, r.Commands.Count);
            Assert.AreEqual(CommandKind.CloseBridge, r.Commands[0].World.Kind);
            Assert.AreEqual(CommandKind.RiverDelta, r.Commands[1].World.Kind);
            Assert.AreEqual(0.5f, r.Commands[1].World.FloatArg, 0.001f);
        }

        [Test]
        public void AmbiguousInsideCompound_KeepsTheOtherPart()
        {
            var r = Parse("подними реку на метр и закрой мост");
            Assert.AreEqual(ParseStatus.Ambiguous, r.Status);
            StringAssert.StartsWith("подними реку на 1 м и закрой", r.Options[0].Text);
        }

        [TestCase("отмени", SpecKind.Undo)]
        [TestCase("undo", SpecKind.Undo)]
        [TestCase("сбрось мир", SpecKind.ResetWorld)]
        [TestCase("пауза", SpecKind.Pause)]
        [TestCase("помощь", SpecKind.Help)]
        [TestCase("покажи маяк", SpecKind.Show)]
        public void SessionCommands(string text, SpecKind kind)
        {
            var r = Parse(text);
            Assert.AreEqual(ParseStatus.Ok, r.Status, r.Message);
            Assert.AreEqual(kind, r.Commands[0].Kind);
        }

        [TestCase("привет")]
        [TestCase("сделай бутерброд")]
        [TestCase("закрой школу")]
        [TestCase("подожги реку")]
        [TestCase("hello there")]
        public void UnknownOrImpossible_IsNotGuessed(string text)
        {
            var r = Parse(text);
            Assert.AreNotEqual(ParseStatus.Ok, r.Status, text);
            Assert.IsNotEmpty(r.Message);
        }

        [Test]
        public void Tokenizer_SplitsUnitsAndKeepsDecimals()
        {
            CollectionAssert.AreEqual(new[] { "подними", "на", "1.5", "м" }, CommandGrammar.Tokenize("Подними на 1,5м"));
            CollectionAssert.AreEqual(new[] { "в", "18:30" }, CommandGrammar.Tokenize("в 18:30"));
        }

        [Test]
        public void CanonicalTextsParseBack()
        {
            var city = TestUtil.City();
            var cmds = new[]
            {
                new WorldCommand(CommandKind.CloseBridge, 2), new WorldCommand(CommandKind.OpenBridge, 1),
                new WorldCommand(CommandKind.SetWeather, 3), new WorldCommand(CommandKind.RiverDelta, 0, 1.5f),
                new WorldCommand(CommandKind.RiverDelta, 0, -0.5f), new WorldCommand(CommandKind.SetTime, 0, 19.5f),
                new WorldCommand(CommandKind.SetTime, 0, 15.25f), new WorldCommand(CommandKind.SetSeason, 2),
                new WorldCommand(CommandKind.LotToPark), new WorldCommand(CommandKind.ParkToLot),
                new WorldCommand(CommandKind.StartFire, city.ChurchBuilding), new WorldCommand(CommandKind.Robbery, city.BankBuilding)
            };
            foreach (var c in cmds)
            {
                string text = CommandParser.Canonical(c, city);
                var w = Single(text);
                Assert.AreEqual(c.Kind, w.Kind, text);
                Assert.AreEqual(c.IntArg, w.IntArg, text);
                Assert.AreEqual(c.FloatArg, w.FloatArg, 0.01f, text);
            }
        }
    }
}
