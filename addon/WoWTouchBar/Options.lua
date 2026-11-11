-- WoWTouchBar: options (Phase 4) - Esc > Options > AddOns > WoW Touch Bar, or /wtb options.
-- Prefs: what is on (pure; tested). Panel: standard checkboxes on a canvas registered with the modern Settings API
-- (present on Forever, feasibility G14). Panel building is guarded: if anything is missing the addon keeps working
-- and the /wtb commands still do the same job. Choices reach the app as a DISPLAY message through the strip.
local _, ns = ...
local Prefs, Options = {}, {}
ns.Prefs, ns.Options = Prefs, Options

Prefs.PAGES = {
  { key = "quest", label = "Quest", bit = 1 },
  { key = "xp", label = "XP", bit = 2 },
  { key = "bags", label = "Bags, durability, gold box", bit = 4 }, -- shown beside the tabs on every page
  { key = "rep", label = "Reputation", bit = 8 },
  -- Notes / timers tabs only appear while there is something on them, so they don't count as "a page to show".
  { key = "notes", label = "Notes (when there are some)", bit = 16, optional = true },
  { key = "timers", label = "Timers (when there are some)", bit = 32, optional = true },
}
Prefs.ALERTS = {
  { key = "questComplete", label = "Quest complete", bit = 1 },
  { key = "bagsFull", label = "Bags full", bit = 2 },
  { key = "repair", label = "Repair soon", bit = 4 },
  { key = "levelUp", label = "Level up", bit = 8 },
  { key = "mail", label = "New mail", bit = 16 },
  { key = "readyCheck", label = "Ready check", bit = 32 },
  { key = "dungeonReady", label = "Dungeon ready", bit = 64 },
}

function Prefs.page(s, key) return not (s.pages and s.pages[key] == false) end
function Prefs.alert(s, key) return not (s.alerts and s.alerts[key] == false) end

--- Turns a page on or off. The last visible page can't be turned off (the bar needs something to show).
function Prefs.setPage(s, key, on)
  local optional = false
  for _, p in ipairs(Prefs.PAGES) do if p.key == key then optional = p.optional == true end end
  if not on and not optional then
    local others = 0
    for _, p in ipairs(Prefs.PAGES) do if p.key ~= key and not p.optional and Prefs.page(s, p.key) then others = others + 1 end end
    if others == 0 then return false end
  end
  s.pages = s.pages or {}
  if on then s.pages[key] = nil else s.pages[key] = false end -- only "off" is stored; new pages default to on
  return true
end

function Prefs.setAlert(s, key, on)
  s.alerts = s.alerts or {}
  if on then s.alerts[key] = nil else s.alerts[key] = false end
end

--- Bit masks for the DISPLAY message.
function Prefs.display(s)
  local pages, alerts = 0, 0
  for _, p in ipairs(Prefs.PAGES) do if Prefs.page(s, p.key) then pages = pages + p.bit end end
  for _, a in ipairs(Prefs.ALERTS) do if Prefs.alert(s, a.key) then alerts = alerts + a.bit end end
  return { pages = pages, alerts = alerts }
end

-- ---------------------------------------------------------------- hiding the game's own UI (optional, off by default)
-- Frame names verified on Forever 2026-11-09 (feasibility G16); none of them is protected, so hiding them is a plain
-- visual change. Once hidden, the frame is hidden again whenever the game shows it; unticking shows it again.
--- WoW's chat window, tabs and chat buttons (names from probe v6, feasibility G17). The typing box
--- (ChatFrame1EditBox) is NOT in the list: its parent is UIParent, so typing keeps working while chat is hidden.
local function chatFrames()
  local list = { "GeneralDockManager", "ChatFrameMenuButton", "ChatFrameChannelButton", "QuickJoinToastButton" }
  local n = tonumber(rawget(_G, "NUM_CHAT_WINDOWS")) or 10
  for i = 1, n do
    list[#list + 1] = "ChatFrame" .. i
    list[#list + 1] = "ChatFrame" .. i .. "Tab"
  end
  return list
end

Prefs.GAME_UI = {
  { key = "hideTracker", label = "Hide quest tracker", frames = { "ObjectiveTrackerFrame" } },
  { key = "hideXpBars", label = "Hide XP / rep bars", frames = { "MainStatusTrackingBarContainer", "SecondaryStatusTrackingBarContainer" } },
  -- Chat is hidden, and comes back while you type (whispers are on the Touch Bar anyway).
  { key = "hideChat", label = "Hide chat (shows while typing)", frames = chatFrames, whileTyping = true },
}

local hooked, wasShown = {}, {}
local typing = false -- the chat typing box has focus

local function framesOf(g) return type(g.frames) == "function" and g.frames() or g.frames end

local function wanted(name)
  local s = ns.State and ns.State.settings() or {}
  for _, g in ipairs(Prefs.GAME_UI) do
    if s[g.key] and not (g.whileTyping and typing) then
      for _, f in ipairs(framesOf(g)) do if f == name then return true end end
    end
  end
  return false
end

--- The typing box stays usable (never hidden, so Enter still opens it) but is made see-through while not typing:
--- Otherwise its empty frame stays behind when chat is hidden.
local boxFaded = false
local function fadeTypingBox(box, s)
  if type(box) ~= "table" or type(box.SetAlpha) ~= "function" then return end
  if s.hideChat and not typing then
    box:SetAlpha(0); boxFaded = true
  elseif boxFaded then
    box:SetAlpha(1); boxFaded = false
  end
end

--- Shows the hidden chat while the typing box has focus (Enter) and hides it again when it loses focus.
local typingHooked = false
local function hookTyping()
  if typingHooked then return end
  local box = rawget(_G, "ChatFrame1EditBox")
  if type(box) ~= "table" or type(box.HookScript) ~= "function" then return end
  local function later() -- WoW may restyle the box right after focus changes; apply again on the next frame
    local timer = rawget(_G, "C_Timer")
    if type(timer) == "table" and type(timer.After) == "function" then timer.After(0, function() pcall(Prefs.applyGameUi) end) end
  end
  box:HookScript("OnEditFocusGained", function() typing = true; pcall(Prefs.applyGameUi) end)
  box:HookScript("OnEditFocusLost", function() typing = false; pcall(Prefs.applyGameUi); later() end)
  box:HookScript("OnShow", function() pcall(Prefs.applyGameUi) end)
  typingHooked = true
end

--- Applies the "hide the game's ..." options. Missing or protected frames are left alone. Returns frames hidden.
function Prefs.applyGameUi()
  local s = ns.State and ns.State.settings() or {}
  if s.hideChat then hookTyping() end
  fadeTypingBox(rawget(_G, "ChatFrame1EditBox"), s)
  local hidden = 0
  for _, g in ipairs(Prefs.GAME_UI) do
    for _, name in ipairs(framesOf(g)) do
      local f = rawget(_G, name)
      local protected = type(f) == "table" and type(f.IsProtected) == "function" and f:IsProtected()
      if type(f) == "table" and type(f.Hide) == "function" and not protected then
        if not hooked[name] and type(f.HookScript) == "function" then
          f:HookScript("OnShow", function(self) if wanted(name) then wasShown[name] = true; self:Hide() end end)
          hooked[name] = true
        end
        if wanted(name) then
          if f:IsShown() then wasShown[name] = true; f:Hide() end
          hidden = hidden + 1
        elseif wasShown[name] then
          wasShown[name] = nil
          f:Show()
        end
      end
    end
  end
  return hidden
end

-- Test hook
function Prefs.isTyping() return typing end

-- State module: the display choices travel with the rest of the state (re-sent on change and in keyframes).
ns.Display = { read = function(settings) return Prefs.display(settings) end }

-- ---------------------------------------------------------------- panel
local function changed()
  if ns.State then ns.State.markDirty() end
  pcall(Prefs.applyGameUi)
  if Options.refresh then Options.refresh() end
end

Options.LABEL_WIDTH = 240 -- checkbox labels; the right column starts at 300

--- Builds and registers the panel once. Returns true, or false + reason (then /wtb commands are the fallback).
function Options.build()
  if Options.category then return true end
  local S = rawget(_G, "Settings")
  if type(S) ~= "table" or type(S.RegisterCanvasLayoutCategory) ~= "function" or type(S.RegisterAddOnCategory) ~= "function" then
    return false, "this client has no options panel API"
  end
  local settings = ns.State.settings()
  local panel = CreateFrame("Frame")
  local refreshers = {}

  local function text(str, x, y, font)
    local fs = panel:CreateFontString(nil, "ARTWORK", font or "GameFontHighlight")
    fs:SetPoint("TOPLEFT", panel, "TOPLEFT", x, y)
    fs:SetText(str)
    return fs
  end

  local function checkbox(label, x, y, get, set)
    local cb = CreateFrame("CheckButton", nil, panel, "UICheckButtonTemplate")
    cb:SetPoint("TOPLEFT", panel, "TOPLEFT", x, y)
    local fs = panel:CreateFontString(nil, "ARTWORK", "GameFontHighlight")
    fs:SetPoint("LEFT", cb, "RIGHT", 4, 0)
    fs:SetText(label)
    pcall(fs.SetWidth, fs, Options.LABEL_WIDTH) -- never wider than its column (long text is cut with "...")
    pcall(fs.SetWordWrap, fs, false)
    pcall(fs.SetJustifyH, fs, "LEFT")
    cb:SetScript("OnClick", function(self)
      set(self:GetChecked() and true or false)
      changed()
    end)
    refreshers[#refreshers + 1] = function() cb:SetChecked(get()) end
    return cb
  end

  text("WoW Touch Bar", 16, -16, "GameFontNormalLarge")
  text("What the Touch Bar shows. Changes apply at once.", 16, -40)

  local y = -72
  text("Pages (tabs on the bar)", 16, y, "GameFontNormal")
  for i, p in ipairs(Prefs.PAGES) do
    checkbox(p.label, 16, y - 4 - i * 26, function() return Prefs.page(settings, p.key) end,
      function(on) if not Prefs.setPage(settings, p.key, on) then print("|cff8c3cdcWoWTouchBar|r at least one page has to stay on.") end end)
  end

  y = y - 4 - (#Prefs.PAGES + 1) * 26 - 10
  text("Chat on the bar", 16, y, "GameFontNormal")
  local chat = function() settings.chat = settings.chat or {}; return settings.chat end
  checkbox("Whispers", 16, y - 30, function() return ns.Chat.settings(settings).whispers end, function(on) chat().whispers = on end)
  local guildModes = { { "off", "Guild: off" }, { "mentions", "Guild: when I'm named" }, { "all", "Guild: everything" } }
  for i, m in ipairs(guildModes) do
    checkbox(m[2], 16, y - 30 - i * 26, function() return ns.Chat.settings(settings).guild == m[1] end,
      function() chat().guild = m[1] end) -- a radio group: picking one switches the others off on refresh
  end

  y = y - 30 - (#guildModes + 1) * 26 - 10
  text("Which quest", 16, y, "GameFontNormal")
  local policies = { { "auto", "Automatic" }, { "watched", "Only watched quests" } }
  for i, m in ipairs(policies) do
    checkbox(m[2], 16, y - 4 - i * 26, function() return (settings.questPolicy or "auto") == m[1] end,
      function() settings.questPolicy = m[1] end)
  end

  local x2 = 300
  text("Alerts", x2, -72, "GameFontNormal")
  for i, a in ipairs(Prefs.ALERTS) do
    checkbox(a.label, x2, -76 - i * 26, function() return Prefs.alert(settings, a.key) end,
      function(on) Prefs.setAlert(settings, a.key, on) end)
  end

  local gy = -76 - (#Prefs.ALERTS + 1) * 26 - 10
  text("Game interface", x2, gy, "GameFontNormal")
  for i, g in ipairs(Prefs.GAME_UI) do
    checkbox(g.label, x2, gy - 4 - i * 26, function() return settings[g.key] == true end,
      function(on) settings[g.key] = on or nil end)
  end

  local ny = gy - 4 - (#Prefs.GAME_UI + 1) * 26 - 10
  text("Notes & timers", x2, ny, "GameFontNormal")
  local open = CreateFrame("Button", nil, panel, "UIPanelButtonTemplate")
  open:SetSize(180, 24)
  open:SetPoint("TOPLEFT", panel, "TOPLEFT", x2 + 4, ny - 22)
  open:SetText("Open notes & timers...")
  open:SetScript("OnClick", function() if ns.NotesWindow then pcall(ns.NotesWindow.toggle) end end)

  Options.refresh = function() for _, r in ipairs(refreshers) do pcall(r) end end
  panel:SetScript("OnShow", Options.refresh)
  Options.refresh()

  local category = S.RegisterCanvasLayoutCategory(panel, "WoW Touch Bar")
  if not category then return false, "the options panel could not be registered" end
  S.RegisterAddOnCategory(category)
  Options.category, Options.panel = category, panel
  return true
end

--- Opens the panel (builds it if needed). Returns true, or false + reason.
function Options.open()
  local ok, built, why = pcall(Options.build)
  if not ok then return false, tostring(built) end
  if not built then return false, why end
  local S, c = rawget(_G, "Settings"), Options.category
  local id = (type(c.GetID) == "function" and c:GetID()) or c.ID
  local opened = pcall(S.OpenToCategory, id)
  return opened, opened and nil or "could not open it - use Esc > Options > AddOns"
end
