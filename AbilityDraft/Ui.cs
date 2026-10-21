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

        string lang = LangOf(s.Slot);
        string Line(int i) => s.Offer[i] is { } a ? $"[{i + 1}]  {a.Title(lang)}  ·  {a.HeroTitle(lang)}   (x{s.Rerolls[i]})" : $"[{i + 1}]  —";
        var title = Tl(lang, s.Round == Slots - 1 ? "menu.title_ult" : "menu.title", s.Round + 1, Slots);
        var keys = Tl(lang, "menu.keys");
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
        string lang = LangOf(s.Slot);
        var row = UI.Horizontal("ad_row").WithStyles(("horizontal-align", "center"), ("margin-top", "24px"));
        for (int i = 0; i < Offers; i++)
        {
            var a = s.Offer[i];
            var card = UI.Vertical($"ad_card{i}").WithStyles(CardStyle);
            if (a != null)
                card.Add(
                    UI.Image($"ad_icon{i}", a.Icon).WithStyles(("width", "120px"), ("height", "120px"), ("horizontal-align", "center")),
                    UI.Label($"ad_name{i}", a.Title(lang)).WithStyles(("font-size", "26px"), ("color", "#f5e6c8"), ("horizontal-align", "center"), ("margin-top", "12px"), ("text-align", "center")),
                    UI.Label($"ad_hero{i}", a.HeroTitle(lang)).WithStyles(("font-size", "18px"), ("color", "#9c9484"), ("horizontal-align", "center")),
                    UI.Button($"ad_pick{i}", Tl(lang, "menu.pick", i + 1), "pick", i.ToString())
                        .WithStyles(ButtonStyle).WithHoverStyle("background-color", "#4f8a4a").WithTextStyles(ButtonText));
            var rr = UI.Button($"ad_rr{i}", Tl(lang, "menu.reroll", s.Rerolls[i]), "reroll", i.ToString())
                .WithStyles(ButtonStyle).WithStyles(("background-color", s.Rerolls[i] > 0 ? "#5a4a2c" : "#2b2b2b"), ("border", "1px solid #b08d57"))
                .WithTextStyles(ButtonText);
            card.Add(rr);
            row.Add(card);
        }

        var kit = string.Join("  ·  ", Enumerable.Range(0, Slots).Select(i =>
            s.Kit[i] is { } k ? AbilityPool.Find(k)?.Title(lang) ?? k : i == s.Round ? "?" : "—"));
        return Frame(
            UI.Label("ad_title", Tl(lang, s.Round == Slots - 1 ? "menu.panel_title_ult" : "menu.panel_title", s.Round + 1, Slots)).WithStyles(TitleStyle),
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
        string lang = LangOf(s.Slot);
        if (!HasUi(s))
        {
            SetBillboard(s, Tl(lang, "menu.kit_ready") + "\n \n" + string.Join("\n", s.Kit.Select(k => AbilityPool.Find(k!)?.Title(lang) ?? k))
                + "\n \n" + Tl(lang, "menu.waiting"));
            return;
        }
        var to = RecipientFilter.Single(s.Slot);
        var kit = string.Join("  ·  ", s.Kit.Select(k => AbilityPool.Find(k!)?.Title(lang) ?? k));
        UI.Panel(PanelId).BuildLayout(to, Frame(
            UI.Label("ad_title", Tl(lang, "menu.kit_ready")).WithStyles(TitleStyle),
            UI.Label("ad_kit", kit).WithStyles(("font-size", "24px"), ("color", "#c9c2b3"), ("horizontal-align", "center"), ("margin-top", "16px")),
            UI.Label("ad_wait", Tl(lang, "menu.waiting"))
                .WithStyles(("font-size", "20px"), ("color", "#9c9484"), ("horizontal-align", "center"), ("margin-top", "16px"))));
    }

    bool _formatSlidesIn;       // set for the one redraw in which the format step appears

    void ShowVote(Seat s)
    {
        var c = Players.FromSlot(s.Slot);
        if (c == null) return;
        string lang = LangOf(s.Slot);
        if (!s.VoteExplained)
        {
            s.VoteExplained = true;
            Chat.PrintToChat(c, Tl(lang, _voteStep == 1 ? "vote.chat_rules" : "vote.chat_format"));
        }
        Format[] formats = [Format.Draft, Format.Chaos, Format.Shuffle];
        if (!HasUi(s))
        {
            string Mark(bool on) => on ? "  ✔" : "";
            string text = _voteStep == 1
                ? Tl(lang, "vote.board_rules") + $"\n \n[1]  Standard{Mark(s.Vote == Rules.Standard)}\n[2]  Street Brawl{Mark(s.Vote == Rules.StreetBrawl)}"
                : Tl(lang, "vote.board_rules_set", RulesName(_rules)) + "\n \n" + Tl(lang, "vote.board_format") + "\n \n"
                    + string.Join("\n", formats.Select((f, n) => $"[{n + 3}]  {FormatName(f, lang)}{Mark(s.FormatVote == f)}"));
            SetBillboard(s, text);
            return;
        }

        var to = RecipientFilter.Single(s.Slot);
        UI.Panel(PanelId).BuildLayout(to, VoteLayout(s));
        if (_formatSlidesIn)
        {
            // The format card is built below its place and see-through, the rules card at full strength: the one
            // rides up over the other, which fades until it only just shows.
            UI.Panel(PanelId).Animate(to, "ad_formats", "transform", "translateY(0px)", "0.45s", "ease-out");
            UI.Panel(PanelId).Animate(to, "ad_formats", "opacity", "1", "0.45s", "ease-out");
            UI.Panel(PanelId).Animate(to, "ad_rules", "opacity", RulesBehind, "0.45s", "ease-out");
        }
        if (!s.PanelUp) UI.Panel(PanelId).RequestCursor(to);
        s.PanelUp = true;
    }

    /// <summary>Everybody's panel again: a vote shows up under its tile on every screen.</summary>
    void RefreshVote()
    {
        foreach (var s in _seats.Values.Where(s => !s.Bot)) ShowVote(s);
    }

    // ---- the look ---------------------------------------------------------------------------------------------------
    // Every window follows one pattern: a dark sheet with a pale hairline, a heavy title with a line of small capitals
    // beside it and a rule under both; inside, cards that fade from a team's colour at the top into the dark, with a
    // bright edge on top; grey-brown tiles; outlined buttons; and a blue strip along the bottom for the hint.
    const string VoteLine = "#4d4841", VoteText = "#f3e6c8", VoteDim = "#b7ae9c", VoteChosen = "#f0b94a";
    const string SheetFill = "#1b1916f2", TileFill = "#3a352f", TileEdge = "#5a544c", RowFill = "#00000066";
    const string AmberEdge = "#e59a2f", SapphireEdge = "#7b96f5", StripFill = "#1e2c74", StripText = "#d6dcff";
    const string RulesBehind = "0.18";          // how much of the rules card shows from under the format card

    static string Fade(string from, string to) => $"gradient( linear, 0% 0%, 0% 100%, from( {from} ), to( {to} ) )";
    static readonly string AmberFill = Fade("#8a5414", "#2a1906"), SapphireFill = Fade("#2c3d86", "#121834");
    static readonly string ChosenFill = Fade("#7a5f22", "#33280f"), TileHover = Fade("#4b453d", "#3a352f");

    /// <summary>A window: the sheet, the title line, the body and, when given, the hint strip.</summary>
    static UINode Sheet(string id, string title, IEnumerable<UINode> beside, UINode body, string? hint, params (string, string)[] place)
    {
        var head = UI.Horizontal(id + "_head").WithStyles(("width", "100%"), ("height", "62px"));
        head.Add(UI.Label(id + "_title", title).WithStyles(("font-size", "32px"), ("font-weight", "black"), ("letter-spacing", "2px"), ("color", "#ffffff"),
            ("vertical-align", "center"), ("margin-left", "18px"), ("margin-right", "10px")));
        foreach (var node in beside) head.Add(node);
        var sheet = UI.Vertical(id).WithStyles(("background-color", SheetFill), ("border", $"1px solid {VoteLine}"), ("box-shadow", "#000000cc 0px 0px 28px 0px")).WithStyles(place);
        sheet.Add(head, UI.Container(id + "_rule").WithStyles(("width", "100%"), ("height", "1px"), ("background-color", VoteLine), ("margin", "0px 16px")), body);
        if (hint != null)
            sheet.Add(UI.Container(id + "_strip").WithStyles(("width", "100%"), ("background-color", StripFill), ("margin", "4px 16px 14px 16px")).Add(
                UI.Label(id + "_hint", hint).WithStyles(("font-size", "15px"), ("color", StripText), ("margin", "7px 10px"), ("white-space", "normal"))));
        return sheet;
    }

    /// <summary>The small capitals beside a window's title.</summary>
    static UINode Caps(string id, string text, string colour = VoteDim) =>
        UI.Label(id, text).WithStyles(("font-size", "16px"), ("color", colour), ("letter-spacing", "2px"), ("vertical-align", "center"), ("margin-left", "16px"));

    /// <summary>A card in a team's colour: bright edge on top, the colour fading into the dark below.</summary>
    static UINode Card(string id, string title, bool amber, UINode content, params (string, string)[] place) =>
        UI.Vertical(id).WithStyles(("background-color", amber ? AmberFill : SapphireFill), ("border-top", $"2px solid {(amber ? AmberEdge : SapphireEdge)}")).WithStyles(place).Add(
            UI.Label(id + "_title", title).WithStyles(("font-size", "22px"), ("font-weight", "black"), ("letter-spacing", "2px"), ("color", VoteText), ("margin", "10px 12px 4px 12px")),
            content);

    /// <summary>An outlined button, as the game's own JOIN and CLOSE.</summary>
    static UIButton Outlined(string id, string text, string action, string arg, bool chosen = false) =>
        UI.Button(id, text, action, arg)
            .WithStyles(("height", "40px"), ("background-color", chosen ? ChosenFill : "#00000044"), ("border", $"1px solid {(chosen ? VoteChosen : "#d8d2c4")}"))
            .WithHoverStyle("background-color", "#ffffff22")
            .WithTextStyles(("font-size", "18px"), ("letter-spacing", "1px"), ("color", chosen ? VoteChosen : "#ffffff"), ("horizontal-align", "center"),
                ("vertical-align", "center"), ("text-transform", "uppercase"), ("margin", "0px 14px"));

    UINode Portraits(string id, IEnumerable<Seat> who, Seat viewer)
    {
        var row = UI.Horizontal(id).WithStyles(("horizontal-align", "center"), ("height", "44px"), ("margin-top", "6px"));
        int n = 0;
        foreach (var seat in who)
        {
            string? icon = HeroIcon(seat.Slot);
            if (icon != null)
                row.Add(UI.Image($"{id}_{n++}", icon).WithStyles(("width", "40px"), ("height", "40px"), ("margin", "0px 2px"),
                    ("border-radius", "50%"), ("border", $"2px solid {(seat == viewer ? VoteChosen : TileEdge)}")));
            else
                row.Add(UI.Label($"{id}_{n++}", "●").WithStyles(("font-size", "26px"), ("color", seat == viewer ? VoteChosen : VoteDim), ("margin", "0px 4px")));
        }
        return row;
    }

    static string? HeroIcon(int slot) =>
        Players.FromSlot(slot)?.GetHeroPawn() is { } pawn && pawn.HeroID != 0
            ? AbilityPool.HeroIcons.GetValueOrDefault("hero_" + HeroKey(pawn.HeroID.ToHeroName())) : null;

    // ---- the vote ------------------------------------------------------------------------------------------------------
    // Step one is the rules card on its own. In step two the format card rides up over it from below, and the rules
    // card stays underneath, faded, with its top edge showing.
    const int TileWidth = 208, CardHeight = 262, CardPeek = 40;

    UINode VoteLayout(Seat s)
    {
        string lang = LangOf(s.Slot);
        var humans = _seats.Values.Where(x => !x.Bot).ToList();
        int voted = _voteStep == 1 ? humans.Count(x => x.Vote != Rules.None) : humans.Count(x => x.FormatVote != Format.None);

        // "live" tiles take a click; the rules tiles of a finished step only show how it went.
        UINode Tile(string id, int digit, string title, string hint, string action, string arg, bool chosen, bool live, IEnumerable<Seat> who) =>
            UI.Vertical(id).WithStyles(("width", $"{TileWidth}px"), ("margin", "0px 6px")).Add(
                UI.Button(id + "_b", title, live ? action : "none", arg)
                    .WithStyles(("width", "100%"), ("height", "92px"), ("background-color", chosen ? ChosenFill : TileFill),
                        ("border", $"{(chosen ? 2 : 1)}px solid {(chosen ? VoteChosen : TileEdge)}"))
                    .WithHoverStyle("background-color", live && !chosen ? TileHover : chosen ? ChosenFill : TileFill)
                    .WithTextStyles(("font-size", "24px"), ("font-weight", "black"), ("letter-spacing", "1px"), ("color", chosen ? VoteChosen : VoteText),
                        ("horizontal-align", "center"), ("vertical-align", "center"), ("text-transform", "uppercase")),
                UI.Label(id + "_h", live ? $"[{digit}]  {hint}" : hint).WithStyles(("font-size", "15px"), ("color", VoteDim), ("horizontal-align", "center"),
                    ("text-align", "center"), ("margin-top", "6px"), ("height", "38px"), ("width", "100%"), ("white-space", "normal"), ("text-overflow", "clip")),
                Portraits(id + "_v", who, s));

        UINode Tiles(string id, params UINode[] tiles)
        {
            var row = UI.Horizontal(id).WithStyles(("margin", "8px 8px 10px 8px"), ("horizontal-align", "center"));
            row.Add(tiles);
            return row;
        }

        bool rulesLive = _voteStep == 1, rulesVoted = _rulesDecided && _voteStd + _voteBrawl > 0;
        UINode? rules = rulesLive || rulesVoted
            ? Card("ad_rules", Tl(lang, "vote.rules"), true, Tiles("ad_rules_row",
                Tile("ad_std", 1, "Standard", Tl(lang, "vote.standard_hint"), "vote", "standard",
                    rulesLive ? s.Vote == Rules.Standard : _rules == Rules.Standard, rulesLive, humans.Where(x => x.Vote == Rules.Standard)),
                Tile("ad_brawl", 2, "Street Brawl", Tl(lang, "vote.brawl_hint"), "vote", "brawl",
                    rulesLive ? s.Vote == Rules.StreetBrawl : _rules == Rules.StreetBrawl, rulesLive, humans.Where(x => x.Vote == Rules.StreetBrawl))),
                ("horizontal-align", "center"), ("height", $"{CardHeight}px"))
            : null;

        UINode body;
        if (_voteStep == 1) body = UI.Container("ad_stack").WithStyles(("horizontal-align", "center"), ("margin", "16px 16px 8px 16px")).Add(rules!);
        else
        {
            Format[] formats = [Format.Draft, Format.Chaos, Format.Shuffle];
            var format = Card("ad_formats", Tl(lang, "vote.format"), false, Tiles("ad_formats_row", formats.Select((f, n) =>
                Tile($"ad_format{n}", n + 3, FormatName(f, lang), FormatHint(f, lang), "format", f.ToString().ToLowerInvariant(), s.FormatVote == f, true, humans.Where(x => x.FormatVote == f))).ToArray()),
                ("horizontal-align", "center"), ("height", $"{CardHeight}px"), ("margin-top", rules != null ? $"{CardPeek}px" : "0px"));
            // A plain panel lays its children on top of one another: the rules card first, the format card over it.
            var stack = UI.Container("ad_stack").WithStyles(("horizontal-align", "center"), ("margin", "16px 16px 8px 16px"));
            if (rules != null) stack.Add(rules.WithStyles(("opacity", _formatSlidesIn ? "1" : RulesBehind)));
            if (_formatSlidesIn) format.WithStyles(("transform", $"translateY({CardHeight}px)"), ("opacity", "0"));
            stack.Add(format);
            body = stack;
        }

        bool twoSteps = _voteStep == 1 ? _forcedFormat == Format.None : rulesVoted;
        string stage = twoSteps ? Tl(lang, "vote.stage_step", _voteStep) : Tl(lang, "vote.stage");
        UINode[] beside =
        [
            Caps("ad_stage", stage),
            Caps("ad_count", Tl(lang, "vote.count", voted, humans.Count)),
            Caps("ad_timer", VoteClock(), VoteChosen),
        ];
        var sheet = Sheet("ad_window", "ABILITY BRAWL", beside, body,
            Tl(lang, "vote.footer"),
            ("horizontal-align", "center"), ("vertical-align", "center"), ("min-width", "720px"));
        return UI.Container("ad_root").WithStyles(("width", "100%"), ("height", "100%"), ("background-color", "#00000088")).Add(sheet);
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
        FilterItemDeals();
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
