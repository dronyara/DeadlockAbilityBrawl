using DeadworksManaged.Api;
using DeadworksManaged.Api.UI;

namespace AbilityDraft;

// What the player sees. Players who joined through the Deadworks launcher get a clickable panel;
// everyone gets the same offer in chat, so the draft is fully playable with keys 1-3 and R alone.
public sealed partial class DraftPlugin
{
    const string PanelId = "abilitydraft";
    const float BillboardDistance = 260f;    // in front of the hero's eyes, clear of the third-person camera
    const float BillboardRaise = 70f;        // lifted so the hero does not stand in the middle of the text
    const float BillboardScale = 0.2f;

    static readonly (string, string)[] CardStyle =
    [
        ("width", "290px"), ("margin", "0px 12px"), ("padding", "18px"), ("flow-children", "down"),
        ("background-color", "#17140ff2"), ("border", "2px solid #b08d57"), ("border-radius", "10px"),
    ];

    static readonly (string, string)[] ButtonStyle =
    [
        ("width", "100%"), ("height", "46px"), ("margin-top", "10px"), ("border-radius", "6px"),
        ("background-color", "#3d6b3a"), ("border", "1px solid #7fbf6f"),
    ];

    static readonly (string, string)[] ButtonText =
    [
        ("font-size", "22px"), ("color", "#ffffff"), ("horizontal-align", "center"), ("vertical-align", "center"),
    ];

    bool _panelWired;

    // The panels are for players whose client has the Deadworks UI bridge: the optional addon brings it, and so does
    // the Deadworks launcher. Everyone else gets the same choices in chat and as text in front of the hero.
    // Bridge command "panelui 0|1" switches them off and on while the server runs.
    static bool PanelUi = true;

    // The panel channel is only touched once a launcher client is actually present.
    bool HasUi(Seat s)
    {
        if (!PanelUi || s.Bot || !UI.HasClientBootstrap(s.Slot)) return false;
        if (!_panelWired)
        {
            _panelWired = true;
            var panel = UI.Panel(PanelId);
            panel.On("pick", e => OnUiOffer(e, reroll: false));
            panel.On("reroll", e => OnUiOffer(e, reroll: true));
            panel.On("vote", e => { if (e.Caller != null) CastVote(e.Caller.Slot, e.ArgAt(0) == "brawl" ? Rules.StreetBrawl : Rules.Standard); });
            panel.On("format", e => { if (e.Caller != null) CastFormat(e.Caller.Slot, ParseFormat(e.ArgAt(0) ?? "")); });
            UI.ClientResync += OnClientResync;
        }
        return true;
    }

    void ShowOffer(Seat s)
    {
        if (s.Bot) return;
        var c = Players.FromSlot(s.Slot);
        if (c == null) return;

        string Line(int i) => s.Offer[i] is { } a ? $"[{i + 1}]  {a.Title(s.Ru)}  ·  {a.HeroTitle(s.Ru)}   (x{s.Rerolls[i]})" : $"[{i + 1}]  —";
        bool ult = s.Round == Slots - 1;
        var title = s.Ru ? $"СПОСОБНОСТЬ {s.Round + 1}/{Slots}{(ult ? " · УЛЬТА" : "")}" : $"ABILITY {s.Round + 1}/{Slots}{(ult ? " · ULTIMATE" : "")}";
        var keys = s.Ru ? "1-3 — взять    R+1-3 — заменить" : "1-3 pick    R+1-3 reroll";
        Chat.PrintToChat(c, $"{title}: {string.Join("  |  ", Enumerable.Range(0, Offers).Select(Line))}  ·  {keys}");

        if (!HasUi(s))
        {
            // Stock clients have no panel, so the same menu floats over their own hero as world text.
            var lines = string.Join("\n", Enumerable.Range(0, Offers).Select(Line));
            SetBillboard(s, $"{title}\n \n{lines}\n \n{keys}");
            return;
        }
        var to = RecipientFilter.Single(s.Slot);
        UI.Panel(PanelId).BuildLayout(to, OfferLayout(s));
        if (!s.PanelUp) UI.Panel(PanelId).RequestCursor(to);
        s.PanelUp = true;
    }

    UINode OfferLayout(Seat s)
    {
        bool ru = s.Ru;
        var row = UI.Horizontal("ad_row").WithStyles(("horizontal-align", "center"), ("margin-top", "24px"));
        for (int i = 0; i < Offers; i++)
        {
            var a = s.Offer[i];
            var card = UI.Vertical($"ad_card{i}").WithStyles(CardStyle);
            if (a != null)
                card.Add(
                    UI.Image($"ad_icon{i}", a.Icon).WithStyles(("width", "120px"), ("height", "120px"), ("horizontal-align", "center")),
                    UI.Label($"ad_name{i}", a.Title(ru)).WithStyles(("font-size", "26px"), ("color", "#f5e6c8"), ("horizontal-align", "center"), ("margin-top", "12px"), ("text-align", "center")),
                    UI.Label($"ad_hero{i}", a.HeroTitle(ru)).WithStyles(("font-size", "18px"), ("color", "#9c9484"), ("horizontal-align", "center")),
                    UI.Button($"ad_pick{i}", ru ? $"Взять [{i + 1}]" : $"Pick [{i + 1}]", "pick", i.ToString())
                        .WithStyles(ButtonStyle).WithHoverStyle("background-color", "#4f8a4a").WithTextStyles(ButtonText));
            var rr = UI.Button($"ad_rr{i}", (ru ? "Заменить" : "Reroll") + $" ({s.Rerolls[i]})", "reroll", i.ToString())
                .WithStyles(ButtonStyle).WithStyles(("background-color", s.Rerolls[i] > 0 ? "#5a4a2c" : "#2b2b2b"), ("border", "1px solid #b08d57"))
                .WithTextStyles(ButtonText);
            card.Add(rr);
            row.Add(card);
        }

        var kit = string.Join("  ·  ", Enumerable.Range(0, Slots).Select(i =>
            s.Kit[i] is { } k ? AbilityPool.Find(k)?.Title(ru) ?? k : i == s.Round ? "?" : "—"));
        return Frame(
            UI.Label("ad_title", ru
                ? $"ВЫБОР СПОСОБНОСТИ {s.Round + 1}/{Slots}{(s.Round == Slots - 1 ? " — УЛЬТА" : "")}"
                : $"ABILITY DRAFT {s.Round + 1}/{Slots}{(s.Round == Slots - 1 ? " — ULTIMATE" : "")}").WithStyles(TitleStyle),
            UI.Label("ad_timer", "").WithStyles(("font-size", "30px"), ("color", "#e8b04a"), ("horizontal-align", "center")),
            row,
            UI.Label("ad_kit", kit).WithStyles(("font-size", "20px"), ("color", "#c9c2b3"), ("horizontal-align", "center"), ("margin-top", "22px")));
    }

    static readonly (string, string)[] TitleStyle =
    [
        ("font-size", "40px"), ("color", "#f5e6c8"), ("horizontal-align", "center"), ("letter-spacing", "3px"),
    ];

    static UINode Frame(params UINode[] children)
    {
        var box = UI.Vertical("ad_box").WithStyles(("horizontal-align", "center"), ("vertical-align", "center"));
        box.Add(children);
        return UI.Container("ad_root").WithStyles(("width", "100%"), ("height", "100%"), ("background-color", "#000000b8")).Add(box);
    }

    void ShowWaiting(Seat s)
    {
        if (!HasUi(s))
        {
            SetBillboard(s, (s.Ru ? "НАБОР ГОТОВ\n \n" : "KIT READY\n \n") + string.Join("\n", s.Kit.Select(k => AbilityPool.Find(k!)?.Title(s.Ru) ?? k))
                + (s.Ru ? "\n \nЖдём остальных…" : "\n \nWaiting for the others…"));
            return;
        }
        var to = RecipientFilter.Single(s.Slot);
        var kit = string.Join("  ·  ", s.Kit.Select(k => AbilityPool.Find(k!)?.Title(s.Ru) ?? k));
        UI.Panel(PanelId).BuildLayout(to, Frame(
            UI.Label("ad_title", s.Ru ? "НАБОР ГОТОВ" : "KIT READY").WithStyles(TitleStyle),
            UI.Label("ad_kit", kit).WithStyles(("font-size", "24px"), ("color", "#c9c2b3"), ("horizontal-align", "center"), ("margin-top", "16px")),
            UI.Label("ad_wait", s.Ru ? "Ждём остальных игроков…" : "Waiting for the other players…")
                .WithStyles(("font-size", "20px"), ("color", "#9c9484"), ("horizontal-align", "center"), ("margin-top", "16px"))));
    }

    void ShowVote(Seat s)
    {
        var c = Players.FromSlot(s.Slot);
        if (c == null) return;
        bool ru = s.Ru, formatOpen = _forcedFormat == Format.None;
        if (!s.VoteExplained)
        {
            s.VoteExplained = true;
            Chat.PrintToChat(c, ru
                ? "[Draft] Правила: напиши в чат 1 — Standard, 2 — Street Brawl (или /vote standard, /vote brawl)"
                : "[Draft] Rules: type 1 for Standard, 2 for Street Brawl (or /vote standard, /vote brawl)");
            if (formatOpen)
                Chat.PrintToChat(c, ru
                    ? "[Draft] Формат: 3 — Драфт, 4 — Хаос (случайные герои и способности), 5 — Перетасовка (способность меняется после применения)"
                    : "[Draft] Format: 3 - Draft, 4 - Chaos (random heroes and abilities), 5 - Shuffle (an ability changes after each use)");
        }
        Format[] formats = [Format.Draft, Format.Chaos, Format.Shuffle];
        if (!HasUi(s))
        {
            string Mark(bool on) => on ? "  ✔" : "";
            string text = (ru ? "ПРАВИЛА МАТЧА" : "MATCH RULES") + $"\n \n[1]  Standard{Mark(s.Vote == Rules.Standard)}\n[2]  Street Brawl{Mark(s.Vote == Rules.StreetBrawl)}";
            if (formatOpen)
                text += "\n \n" + (ru ? "ФОРМАТ" : "FORMAT") + "\n \n" + string.Join("\n", formats.Select((f, n) => $"[{n + 3}]  {FormatName(f, ru)}{Mark(s.FormatVote == f)}"));
            SetBillboard(s, text);
            return;
        }

        var to = RecipientFilter.Single(s.Slot);
        UI.Panel(PanelId).BuildLayout(to, VoteLayout(s, formatOpen));
        if (!s.PanelUp) UI.Panel(PanelId).RequestCursor(to);
        s.PanelUp = true;
    }

    /// <summary>Everybody's panel again: a vote shows up under its tile on every screen.</summary>
    void RefreshVote()
    {
        foreach (var s in _seats.Values.Where(s => !s.Bot)) ShowVote(s);
    }

    // The vote as a window: a header bar, the rules on the left under the amber team's colour, the formats on the
    // right under the sapphire team's, and under every tile the portraits of the heroes who voted for it.
    const string VoteDark = "#16130ff7", VoteLine = "#3b352c", VoteText = "#e9e2d2", VoteDim = "#8f8777";
    const string VoteAmber = "#8a5718", VoteSapphire = "#27406f", VoteChosen = "#d9a441";

    UINode VoteLayout(Seat s, bool formatOpen)
    {
        bool ru = s.Ru;
        var humans = _seats.Values.Where(x => !x.Bot).ToList();
        int voted = humans.Count(x => x.Vote != Rules.None && (!formatOpen || x.FormatVote != Format.None));

        UINode Voters(string id, IEnumerable<Seat> who)
        {
            var row = UI.Horizontal(id).WithStyles(("horizontal-align", "center"), ("height", "44px"), ("margin-top", "8px"));
            int n = 0;
            foreach (var seat in who)
            {
                string? icon = Players.FromSlot(seat.Slot)?.GetHeroPawn() is { } pawn && pawn.HeroID != 0
                    ? AbilityPool.HeroIcons.GetValueOrDefault("hero_" + HeroKey(pawn.HeroID.ToHeroName())) : null;
                if (icon != null)
                    row.Add(UI.Image($"{id}_{n++}", icon).WithStyles(("width", "40px"), ("height", "40px"), ("margin", "0px 2px"),
                        ("border-radius", "50%"), ("border", $"2px solid {(seat == s ? VoteChosen : VoteLine)}")));
                else
                    row.Add(UI.Label($"{id}_{n++}", "●").WithStyles(("font-size", "26px"), ("color", seat == s ? VoteChosen : VoteDim), ("margin", "0px 4px")));
            }
            return row;
        }

        UINode Tile(string id, int digit, string title, string hint, string action, string arg, bool mine, IEnumerable<Seat> who) =>
            UI.Vertical(id).WithStyles(("width", "208px"), ("margin", "0px 6px")).Add(
                UI.Button(id + "_b", $"{title}", action, arg)
                    .WithStyles(("width", "100%"), ("height", "92px"), ("background-color", mine ? "#3a301c" : "#221e18"),
                        ("border", $"2px solid {(mine ? VoteChosen : VoteLine)}"), ("border-radius", "3px"))
                    .WithHoverStyle("background-color", "#40372a")
                    .WithTextStyles(("font-size", "25px"), ("font-weight", "bold"), ("letter-spacing", "1px"), ("color", mine ? VoteChosen : VoteText),
                        ("horizontal-align", "center"), ("vertical-align", "center"), ("text-transform", "uppercase")),
                UI.Label(id + "_h", $"[{digit}]  {hint}").WithStyles(("font-size", "15px"), ("color", VoteDim), ("horizontal-align", "center"),
                    ("text-align", "center"), ("margin-top", "6px"), ("height", "38px"), ("width", "100%"), ("white-space", "normal"), ("text-overflow", "clip")),
                Voters(id + "_v", who));

        UINode Group(string id, string title, string colour, params UINode[] tiles)
        {
            var row = UI.Horizontal(id + "_row").WithStyles(("margin", "14px 8px 10px 8px"));
            row.Add(tiles);
            return UI.Vertical(id).WithStyles(("margin", "0px 8px"), ("background-color", "#1d1913"), ("border", $"1px solid {VoteLine}")).Add(
                UI.Container(id + "_head").WithStyles(("width", "100%"), ("height", "44px"), ("background-color", colour)).Add(
                    UI.Label(id + "_title", title).WithStyles(("font-size", "22px"), ("font-weight", "bold"), ("letter-spacing", "2px"),
                        ("color", "#ffffff"), ("margin-left", "14px"), ("vertical-align", "center"))),
                row);
        }

        var groups = UI.Horizontal("ad_groups").WithStyles(("horizontal-align", "center"), ("margin", "18px 10px 6px 10px"));
        groups.Add(Group("ad_rules", ru ? "ПРАВИЛА" : "RULES", VoteAmber,
            Tile("ad_std", 1, "Standard", ru ? "обычный матч" : "the full match", "vote", "standard", s.Vote == Rules.Standard, humans.Where(x => x.Vote == Rules.Standard)),
            Tile("ad_brawl", 2, "Street Brawl", ru ? "короткие раунды" : "short rounds", "vote", "brawl", s.Vote == Rules.StreetBrawl, humans.Where(x => x.Vote == Rules.StreetBrawl))));
        if (formatOpen)
        {
            Format[] formats = [Format.Draft, Format.Chaos, Format.Shuffle];
            groups.Add(Group("ad_formats", ru ? "ФОРМАТ" : "FORMAT", VoteSapphire, formats.Select((f, n) =>
                Tile($"ad_format{n}", n + 3, FormatName(f, ru), FormatHint(f, ru), "format", f.ToString().ToLowerInvariant(), s.FormatVote == f, humans.Where(x => x.FormatVote == f))).ToArray()));
        }

        (string, string)[] caps = [("font-size", "16px"), ("color", VoteDim), ("letter-spacing", "1px"), ("vertical-align", "center"), ("margin-left", "22px")];
        var header = UI.Horizontal("ad_header").WithStyles(("width", "100%"), ("height", "50px"), ("background-color", "#0d0b09"), ("border-bottom", $"1px solid {VoteLine}")).Add(
            UI.Label("ad_title", "ABILITY BRAWL").WithStyles(("font-size", "24px"), ("font-weight", "bold"), ("letter-spacing", "3px"), ("color", "#ffffff"),
                ("vertical-align", "center"), ("margin-left", "18px")),
            UI.Label("ad_stage", ru ? "ГОЛОСОВАНИЕ" : "VOTE").WithStyles(caps),
            UI.Label("ad_count", ru ? $"ПРОГОЛОСОВАЛО {voted}/{humans.Count}" : $"{voted}/{humans.Count} VOTED").WithStyles(caps),
            UI.Label("ad_timer", VoteClock()).WithStyles(caps).WithStyles(("color", VoteChosen)));

        var window = UI.Vertical("ad_window").WithStyles(("horizontal-align", "center"), ("vertical-align", "center"),
            ("background-color", VoteDark), ("border", $"1px solid {VoteLine}"), ("box-shadow", "#000000cc 0px 0px 24px 0px")).Add(
            header,
            groups,
            UI.Label("ad_foot", ru
                ? "Нажми на плитку или напиши её цифру в чат. При равенстве решает голос лидера лобби."
                : "Click a tile or type its digit in chat. A tie goes to the lobby leader's vote.")
                .WithStyles(("font-size", "16px"), ("color", VoteDim), ("margin", "6px 18px 14px 18px")));
        return UI.Container("ad_root").WithStyles(("width", "100%"), ("height", "100%"), ("background-color", "#00000099")).Add(window);
    }

    string VoteClock()
    {
        int left = Math.Max(0, (int)MathF.Ceiling(_phaseEnd - Now));
        return $"{left / 60}:{left % 60:00}";
    }

    // ---- world-text menu for clients without the panel ----------------------------------------------------------
    void SetBillboard(Seat s, string text)
    {
        if (s.Billboard is { IsValid: true }) { s.Billboard.SetMessage(text); return; }
        var pawn = Players.FromSlot(s.Slot)?.GetHeroPawn();
        if (pawn == null) return;
        var t = CPointWorldText.Create(text, pawn.EyePosition, fontSize: 64, worldUnitsPerPx: BillboardScale);
        if (t == null) { Log($"slot {s.Slot}: world text could not be created"); return; }
        t.JustifyHorizontal = HorizontalJustify.Center;
        t.JustifyVertical = VerticalJustify.Center;
        t.Fullbright = true;
        s.Billboard = t;
        PlaceBillboard(s);
    }

    /// <summary>Holds the menu in front of the player's view, upright and facing them, wherever they look.</summary>
    void PlaceBillboard(Seat s)
    {
        if (s.Billboard is not { IsValid: true } t || Players.FromSlot(s.Slot)?.GetHeroPawn() is not { } pawn) return;
        var view = pawn.CameraAngles;
        float pitch = view.X * MathF.PI / 180f, yaw = view.Y * MathF.PI / 180f;
        var forward = new System.Numerics.Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
        var up = new System.Numerics.Vector3(MathF.Sin(pitch) * MathF.Cos(yaw), MathF.Sin(pitch) * MathF.Sin(yaw), MathF.Cos(pitch));
        // point_worldtext reads along its local axes; this yaw/roll pair turns its face back towards the viewer.
        t.Teleport(pawn.EyePosition + forward * BillboardDistance + up * BillboardRaise, new System.Numerics.Vector3(0, view.Y + 270f, 90f - view.X));
    }

    public override void OnGameFrame(bool simulating, bool firstTick, bool lastTick)
    {
        TickNativeDraft();
        if (_phase is not (Phase.Drafting or Phase.Voting)) return;
        foreach (var s in _seats.Values) PlaceBillboard(s);
    }

    static void ClearBillboard(Seat s)
    {
        if (s.Billboard is { IsValid: true }) s.Billboard.Remove();
        s.Billboard = null;
    }

    // A menu is private: everybody else's billboard is kept off the wire.
    public override void OnCheckTransmit(CheckTransmitEvent args)
    {
        foreach (var s in _seats.Values)
            if (s.Slot != args.PlayerSlot && s.Billboard is { IsValid: true } b) args.Hide(b);
    }

    void HidePanel(Seat s)
    {
        ClearBillboard(s);
        if (!s.PanelUp) return;
        var to = RecipientFilter.Single(s.Slot);
        UI.Panel(PanelId).ReleaseCursor(to);
        UI.Panel(PanelId).DestroyLayout(to);
        s.PanelUp = false;
    }

    void OnUiOffer(UIEvent e, bool reroll)
    {
        if (e.Caller == null || !_seats.TryGetValue(e.Caller.Slot, out var s) || !int.TryParse(e.ArgAt(0), out int i)) return;
        if (reroll) Reroll(s, i); else Pick(s, i);
    }

    // A client that hitched drops its UI and cannot rebuild it: layouts live on the server.
    void OnClientResync(int slot)
    {
        if (!_seats.TryGetValue(slot, out var s) || !s.PanelUp) return;
        s.PanelUp = false;
        if (_phase == Phase.Voting) ShowVote(s);
        else if (_phase == Phase.Drafting) { if (s.Done) ShowWaiting(s); else ShowOffer(s); }
    }
}
