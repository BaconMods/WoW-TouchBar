-- WoWTouchBar: wiring. Game events → State (debounced) → Transport (visible pixel strip).
-- Read-only: nothing here acts on the game; it only reads state and draws an addon-owned frame.
local addonName, ns = ...
local State, Transport = ns.State, ns.Transport

local STATE_REFRESH_SECONDS = 5 -- light periodic refresh in case an event is missed

WoWTouchBarDB = WoWTouchBarDB or {} -- SavedVariables: settings, notes and diagnostics (written on /reload / logout)

State.onChange(Transport.onState)

local events = CreateFrame("Frame")
events:RegisterEvent("UI_SCALE_CHANGED")
events:RegisterEvent("DISPLAY_SIZE_CHANGED")
State.register(events)
events:SetScript("OnEvent", function(_, event)
  if event == "PLAYER_ENTERING_WORLD" then
    Transport.build(State.settings().strip)
    if ns.Debug then pcall(ns.Debug.run); pcall(ns.Debug.watchCombat) end
    pcall(ns.Prefs.applyGameUi) -- "hide the game's quest tracker / XP bars", if chosen
    local ok, built, why = pcall(ns.Options.build) -- so it is listed under Esc > Options > AddOns
    if not ok or not built then State.recordError("Options", ok and why or built) end
  end
  if event == "UI_SCALE_CHANGED" or event == "DISPLAY_SIZE_CHANGED" then Transport.rescale() end
  State.markDirty()
end)

-- Chat: its own frame, so a chat burst never triggers state collects. Messages pass straight to the strip.
local chatEvents = CreateFrame("Frame")
for _, e in ipairs(ns.Chat.EVENTS) do pcall(chatEvents.RegisterEvent, chatEvents, e) end
chatEvents:SetScript("OnEvent", function(_, event, text, sender)
  local ok, err = pcall(ns.Chat.onEvent, event, text, sender)
  if not ok then State.recordError("Chat", err) end
end)

local sinceRefresh = 0 -- whole ticks, not summed seconds (floating-point drift)
local refreshTicks = math.floor(STATE_REFRESH_SECONDS / Transport.HOLD + 0.5)
C_Timer.NewTicker(Transport.HOLD, function()
  local ok, err = pcall(Transport.tick)
  if not ok then State.recordError("Transport", err) end
  sinceRefresh = sinceRefresh + 1
  if sinceRefresh >= refreshTicks then
    sinceRefresh = 0
    State.markDirty()
  end
end)

-- ---------------------------------------------------------------- /wtb panel (chat)
local Panel = {}
ns.Panel = Panel
local TAG = "|cff8c3cdcWoWTouchBar|r "
local SOURCE_NAMES = { pinned = "pinned", supertracked = "super-tracked", selected = "selected", watched = "watched" }

--- Lines describing the normalised state (pure; tested).
function Panel.lines(s)
  local Text = ns.Text
  local out = {}
  if not s then return { "no data yet" } end
  local p = s.player
  if p then
    if p.isMaxLevel then
      out[#out + 1] = ("Level %s (max)"):format(tostring(p.level))
    else
      out[#out + 1] = ("Level %s - XP %s/%s (%.1f%%) - rested %s (%.1f%%)"):format(
        tostring(p.level), tostring(p.xp), tostring(p.xpMax), p.xpPermille / 10, tostring(p.rested), p.restedPermille / 10)
    end
  end
  local q = s.quests
  if q then
    if q.none then
      out[#out + 1] = ("Quest: none tracked (%d watched)"):format(q.watchedCount or 0)
    else
      local status = q.readyForTurnIn and " - ready to turn in" or (q.complete and " - complete" or "")
      out[#out + 1] = ("Quest [%s]: %s%s"):format(SOURCE_NAMES[q.source] or q.source, q.title, status)
      for i, o in ipairs(q.objectives) do
        local mark = o.done and "[done] " or (i == q.currentObjective and "> " or "  ")
        local line = o.numeric and ("%d/%d %s"):format(o.have, o.need, o.label) or o.label
        out[#out + 1] = "  " .. mark .. Text.truncate(line, 70)
      end
    end
  end
  local u = s.utility
  if u then
    local bags = u.bagsFree and ("%d/%d free"):format(u.bagsFree, u.bagsTotal) or "?"
    local dura = u.durabilityLowest and ("%d%% lowest"):format(u.durabilityLowest) or "n/a"
    if (u.durabilityBroken or 0) > 0 then dura = dura .. (", %d broken"):format(u.durabilityBroken) end
    out[#out + 1] = ("Gold %s - Bags %s - Durability %s"):format(u.money and Text.money(u.money) or "?", bags, dura)
  end
  if s.disabled and #s.disabled > 0 then
    out[#out + 1] = "Disabled after an error: " .. table.concat(s.disabled, ", ") .. " (a /reload turns it back on)"
  end
  return out
end

local function show()
  for _, line in ipairs(Panel.lines(State.collect())) do print(TAG .. line) end
end

SLASH_WOWTOUCHBAR1 = "/wtb"
SlashCmdList.WOWTOUCHBAR = function(msg)
  local cmd, arg = (msg or ""):lower():match("^%s*(%S*)%s*(%S*)")
  local settings = State.settings()
  if cmd == "note" or cmd == "timer" then
    -- Notes and timers keep the text as typed (the bar shows capitals anyway).
    local rest = (msg or ""):match("^%s*%S+%s*(.-)%s*$")
    for _, line in ipairs(ns.Notes.command(settings, cmd, rest) or {}) do print(TAG .. line) end
    State.markDirty()
    if ns.NotesWindow then pcall(ns.NotesWindow.refresh) end
  elseif cmd == "notes" or cmd == "timers" then
    local ok, opened = pcall(ns.NotesWindow.toggle)
    if not ok or not opened then for _, line in ipairs(ns.Notes.list(settings)) do print(TAG .. line) end end
  elseif cmd == "pin" then
    local q = State.collect().quests
    if q and q.questID then
      settings.pinnedQuestID = q.questID
      print(TAG .. "pinned: " .. q.title)
    else
      print(TAG .. "no quest to pin")
    end
  elseif cmd == "unpin" then
    settings.pinnedQuestID = nil
    print(TAG .. "unpinned")
  elseif cmd == "sizetest" then
    Transport.startSizeTest(settings.strip)
    print(TAG .. ("size test running (~%d s). Keep WoW in front and the top-left corner uncovered."):format(
      #Transport.TEST_GEOMETRIES * Transport.TEST_SECONDS))
  elseif cmd == "strip" then
    local cell, cols, tone = (msg or ""):lower():match("strip%s+(%d+)%s+(%w+)%s*(%a*)")
    cols = cols == "line" and ns.Protocol.LINE_COLS or tonumber(cols)
    local white = Transport.TONES[tone ~= "" and tone or "bright"]
    if arg == "reset" then
      settings.strip = nil
      Transport.applyGeometry(nil)
      print(TAG .. "strip reset to default")
    elseif cell and cols and ns.Protocol.rowsFor(cols) and white then
      settings.strip = { cell = tonumber(cell), cols = cols, white = white }
      Transport.applyGeometry(settings.strip)
      print(TAG .. ("strip: %s px cells, %s, %s"):format(cell,
        cols == ns.Protocol.LINE_COLS and "colour line" or cols .. " columns", tone ~= "" and tone or "bright"))
    else
      print(TAG .. "usage: /wtb strip <cell 1-8> <line|89|177|353|705> [bright|dark|dim]  or  /wtb strip reset")
    end
  elseif cmd == "options" or cmd == "config" then
    local ok, why = ns.Options.open()
    if not ok then print(TAG .. "options panel: " .. tostring(why) .. ". The /wtb commands still work (/wtb help).") end
  elseif cmd == "help" then
    for _, line in ipairs({
      "/wtb - what the addon sees now",
      "/wtb options - the options panel (also Esc > Options > AddOns > WoW Touch Bar)",
      "/wtb pin | unpin - keep the current quest on the bar",
      "/wtb note <text> - a note on the bar (/wtb note here = where you are; /wtb note del 2; /wtb note clear)",
      "/wtb timer 10m [name] - a timer (90s, 1h30m, 1:30; /wtb timer stop 1 | all)",
      "/wtb notes - the notes & timers window",
      "/wtb policy auto | watched - which quest the bar follows",
      "/wtb chat whispers on|off - /wtb chat guild off|mentions|all",
      "/wtb testchat - two pretend messages to check the bar",
      "/wtb testqueue [ready|off] - a pretend dungeon queue (or dungeon ready) for 60 s",
    }) do print(TAG .. line) end
  elseif cmd == "testqueue" then
    -- Pretend dungeon-finder queue (or "ready") for 60 s, handled like the real thing. Nothing is queued in game.
    local kind = (arg == "ready" or arg == "off") and arg or "queue"
    ns.Status.simulate(kind, 60)
    State.markDirty()
    print(TAG .. ({ queue = "pretend dungeon queue for 60 s (the bar counts up from 1:35).",
      ready = "pretend 'dungeon ready' for 60 s.", off = "pretend queue stopped." })[kind])
  elseif cmd == "testchat" then
    -- Pretend messages, handled exactly like real chat events (nothing is sent in game). For testing without friends online.
    local me = ns.Compat.call("UnitName", "player") or "you"
    ns.Chat.onEvent("CHAT_MSG_WHISPER", "This is a test whisper from WoW Touch Bar. It is long on purpose, so you can see it scroll across the whole Touch Bar.", "Testfriend-Realm")
    ns.Chat.onEvent("CHAT_MSG_GUILD", me .. ", this is a test guild line that mentions you.", "Guildie-Realm")
    print(TAG .. "two test messages sent to the Touch Bar (a whisper, then a guild line). Guild lines follow your /wtb chat setting.")
  elseif cmd == "chat" then
    local what, mode = (msg or ""):lower():match("chat%s+(%a+)%s*(%a*)")
    settings.chat = settings.chat or {}
    if what == "whispers" and (mode == "on" or mode == "off") then
      settings.chat.whispers = mode == "on"
    elseif what == "guild" and ns.Chat.GUILD_MODES[mode] then
      settings.chat.guild = mode
    elseif what then
      print(TAG .. "usage: /wtb chat whispers on|off   /wtb chat guild off|mentions|all")
    end
    local c = ns.Chat.settings(settings)
    print(TAG .. ("chat on the bar: whispers %s, guild chat %s"):format(c.whispers and "on" or "off", c.guild))
  elseif cmd == "policy" then
    if ns.Quests.POLICIES[arg] then
      settings.questPolicy = arg
      print(TAG .. "quest policy: " .. arg)
    else
      print(TAG .. "policies: auto (pinned > super-tracked > selected > watched), watched (pinned > watched)")
    end
  else
    show()
  end
end

ns.addonName = addonName
