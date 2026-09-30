using System;
using System.Collections.Generic;
using SmallTown.Simulation;
using SmallTown.Simulation.Events;

namespace SmallTown.Commands
{
    /// <summary>Everything the UI needs to show after a command.</summary>
    public sealed class ExecResult
    {
        public ParseStatus Status;
        public bool WorldChanged;
        public string Title = "";
        public readonly List<string> Lines = new List<string>();
        public string Question = "";
        public readonly List<ParseOption> Options = new List<ParseOption>();
        public readonly List<CommandSpec> UiActions = new List<CommandSpec>();
        public readonly List<CommandOutcome> Outcomes = new List<CommandOutcome>();
        public string Note = "";
    }

    /// <summary>
    /// Owns the simulation, the parser and the undo stack. Undo restores a full snapshot taken
    /// right before a change (time, RNG, agents), not a reverse replay of consequences.
    /// </summary>
    public sealed class TownSession
    {
        public const int MaxUndo = 12;

        public readonly SimConfig Config;
        public readonly CommandParser Parser;
        public readonly CommandGrammar Grammar;
        public TownSimulation Sim { get; private set; }
        public EntityRef Selected = EntityRef.None;
        public EntityRef Last = EntityRef.None;
        public readonly List<string> History = new List<string>();
        public int CommandsExecuted;
        public CommandKind LastKind;

        private readonly List<byte[]> _undo = new List<byte[]>();
        private readonly List<EntityRef> _undoLast = new List<EntityRef>();

        /// <summary>Raised when <see cref="Sim"/> is replaced (world reset).</summary>
        public event Action SimulationReplaced;

        public TownSession(SimConfig config, CommandGrammar grammar, TownSimulation existing = null)
        {
            Config = config;
            Grammar = grammar;
            Parser = new CommandParser(grammar);
            Sim = existing ?? new TownSimulation(config);
        }

        public int UndoDepth => _undo.Count;
        public bool CanUndo => _undo.Count > 0;

        public ParseContext MakeContext()
        {
            return new ParseContext
            {
                City = Sim.City,
                NorthClosed = Sim.World.NorthClosed,
                SouthClosed = Sim.World.SouthClosed,
                LotIsPark = Sim.World.LotIsPark,
                FireBuilding = Sim.World.FireBuilding,
                Selected = Selected,
                Last = Last
            };
        }

        public ExecResult Execute(string text)
        {
            var res = new ExecResult();
            if (!string.IsNullOrWhiteSpace(text))
            {
                History.Add(text.Trim());
                if (History.Count > 100) History.RemoveAt(0);
            }
            var parsed = Parser.Parse(text, MakeContext());
            res.Status = parsed.Status;
            switch (parsed.Status)
            {
                case ParseStatus.Ambiguous:
                    res.Question = parsed.Message;
                    res.Options.AddRange(parsed.Options);
                    return res;
                case ParseStatus.Unknown:
                case ParseStatus.CannotDo:
                case ParseStatus.Empty:
                    res.Title = parsed.Status == ParseStatus.CannotDo ? "Так я не умею" : "Не понял";
                    res.Note = parsed.Message;
                    return res;
            }
            res.Note = parsed.Message;
            ExecuteSpecs(parsed.Commands, res);
            return res;
        }

        /// <summary>Executes world commands coming directly from UI controls (settings panel, context buttons).</summary>
        public ExecResult ExecuteWorld(WorldCommand cmd)
        {
            var res = new ExecResult { Status = ParseStatus.Ok };
            var spec = new CommandSpec { Kind = SpecKind.World, World = cmd, Canonical = CommandParser.Canonical(cmd, Sim.City) };
            ExecuteSpecs(new List<CommandSpec> { spec }, res);
            return res;
        }

        private void ExecuteSpecs(List<CommandSpec> specs, ExecResult res)
        {
            bool hasWorld = false;
            foreach (var s in specs) if (s.Kind == SpecKind.World) hasWorld = true;
            byte[] snapshot = hasWorld ? Sim.SaveSnapshot() : null;
            var lastBefore = Last;
            bool changed = false;
            var titles = new List<string>();
            var lines = new List<string>();
            foreach (var spec in specs)
            {
                switch (spec.Kind)
                {
                    case SpecKind.World:
                        var o = Sim.Apply(spec.World);
                        res.Outcomes.Add(o);
                        titles.Add(o.Title);
                        for (int i = 0; i + 1 < o.Lines.Count; i++) lines.Add(o.Lines[i]); // status line added once below
                        if (!o.Entity.IsNone) Last = o.Entity;
                        if (o.Changed)
                        {
                            changed = true;
                            LastKind = spec.World.Kind;
                        }
                        break;
                    case SpecKind.Undo:
                        titles.Add(Undo() ? "Отменено: мир вернулся на шаг назад" : "Отменять пока нечего");
                        break;
                    case SpecKind.ResetWorld:
                        ResetWorld();
                        titles.Add("Мир начат заново");
                        break;
                    default:
                        res.UiActions.Add(spec);
                        break;
                }
            }
            if (changed)
            {
                _undo.Add(snapshot);
                _undoLast.Add(lastBefore);
                if (_undo.Count > MaxUndo)
                {
                    _undo.RemoveAt(0);
                    _undoLast.RemoveAt(0);
                }
                CommandsExecuted++;
            }
            res.WorldChanged = changed;
            res.Title = string.Join(" · ", titles);
            int max = Math.Min(3, lines.Count);
            for (int i = 0; i < max; i++) res.Lines.Add(lines[i]);
            if (res.Outcomes.Count > 0) res.Lines.Add(Sim.StatusLine());
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            var data = _undo[_undo.Count - 1];
            var last = _undoLast[_undoLast.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _undoLast.RemoveAt(_undoLast.Count - 1);
            Sim.LoadSnapshot(data);
            Last = last;
            return true;
        }

        public void ResetWorld()
        {
            Sim = new TownSimulation(Config, Sim.City);
            _undo.Clear();
            _undoLast.Clear();
            Selected = EntityRef.None;
            Last = EntityRef.None;
            SimulationReplaced?.Invoke();
        }

        /// <summary>Drops undo entries above <paramref name="depth"/> (used after the showcase restores the world).</summary>
        public void TrimUndo(int depth)
        {
            while (_undo.Count > depth && _undo.Count > 0)
            {
                _undo.RemoveAt(_undo.Count - 1);
                _undoLast.RemoveAt(_undoLast.Count - 1);
            }
        }

        public void PushUndoPoint()
        {
            _undo.Add(Sim.SaveSnapshot());
            _undoLast.Add(Last);
            if (_undo.Count > MaxUndo)
            {
                _undo.RemoveAt(0);
                _undoLast.RemoveAt(0);
            }
        }
    }
}
