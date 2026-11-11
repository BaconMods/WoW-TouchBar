-- WoWTouchBar: a small "Notes & timers" window (/wtb notes, or the button in the options). Phase 7.
-- Plain addon UI: edit boxes and buttons that add to this addon's own lists - nothing in it acts on the game.
-- It only adds: removing happens on the Touch Bar (the x) or in the tray, where the one real list lives.
-- Built on first use and fully guarded: if a template or function is missing, the /wtb note and /wtb timer
-- commands still do the same job (the window templates are standard but not yet checked in game - feasibility G18).
local _, ns = ...
local W = {}
ns.NotesWindow = W

W.WIDTH, W.HEIGHT = 380, 470
local frame
local noteRows, timerRows = {}, {}

local function settings() return ns.State.settings() end
local function changed()
  ns.State.markDirty()
  W.refresh()
end

local function say(line) if line then print("|cff8c3cdcWoWTouchBar|r " .. line) end end

local function button(parent, label, width, onClick)
  local b = CreateFrame("Button", nil, parent, "UIPanelButtonTemplate")
  b:SetSize(width, 22)
  b:SetText(label)
  b:SetScript("OnClick", onClick)
  return b
end

local function input(parent, width, maxLetters)
  local e = CreateFrame("EditBox", nil, parent, "InputBoxTemplate")
  e:SetSize(width, 22)
  e:SetAutoFocus(false)
  if maxLetters then e:SetMaxLetters(maxLetters) end
  e:SetScript("OnEscapePressed", function(self) self:ClearFocus() end)
  return e
end

local function label(parent, text, font)
  local fs = parent:CreateFontString(nil, "ARTWORK", font or "GameFontHighlight")
  fs:SetText(text)
  pcall(fs.SetJustifyH, fs, "LEFT")
  pcall(fs.SetWordWrap, fs, false)
  return fs
end

local function build()
  if frame then return frame end
  local ok, f = pcall(CreateFrame, "Frame", "WoWTouchBarNotesWindow", UIParent, "BasicFrameTemplateWithInset")
  if not ok or not f then f = CreateFrame("Frame", "WoWTouchBarNotesWindow", UIParent) end
  f:SetSize(W.WIDTH, W.HEIGHT)
  f:SetPoint("CENTER")
  f:SetFrameStrata("DIALOG")
  f:SetMovable(true)
  f:EnableMouse(true)
  f:RegisterForDrag("LeftButton")
  f:SetScript("OnDragStart", f.StartMoving)
  f:SetScript("OnDragStop", f.StopMovingOrSizing)
  if not f.TitleText then
    local bg = f:CreateTexture(nil, "BACKGROUND")
    bg:SetAllPoints()
    bg:SetColorTexture(0.06, 0.06, 0.07, 0.95)
  end
  local title = f.TitleText or label(f, "", "GameFontNormal")
  if not f.TitleText then title:SetPoint("TOP", f, "TOP", 0, -6) end
  title:SetText("WoW Touch Bar - notes & timers")
  pcall(table.insert, UISpecialFrames, "WoWTouchBarNotesWindow") -- Esc closes it

  -- Notes: [ text box ] [Add] [Here]
  local y = -34
  label(f, "Add a note (it goes to the NOTE tab)", "GameFontNormal"):SetPoint("TOPLEFT", f, "TOPLEFT", 16, y)
  local note = input(f, 230, ns.Notes.MAX_NOTE_BYTES)
  note:SetPoint("TOPLEFT", f, "TOPLEFT", 22, y - 20)
  local function addNote()
    local okAdd, why = ns.Notes.addNote(settings(), note:GetText())
    if okAdd then note:SetText(""); note:ClearFocus() else say(why) end
    changed()
  end
  note:SetScript("OnEnterPressed", addNote)
  button(f, "Add", 50, addNote):SetPoint("LEFT", note, "RIGHT", 6, 0)
  button(f, "Here", 56, function()
    local where, why = ns.Notes.here()
    if where then note:SetText(where .. " " .. note:GetText()); note:SetFocus() else say(why) end
  end):SetPoint("LEFT", note, "RIGHT", 60, 0)

  for i = 1, ns.Notes.MAX_NOTES do
    local row = { text = label(f, "") }
    row.text:SetPoint("TOPLEFT", f, "TOPLEFT", 20, y - 48 - (i - 1) * 20)
    pcall(row.text.SetWidth, row.text, W.WIDTH - 80)
    noteRows[i] = row
  end

  -- Timers: [time] [name] [Start]
  y = y - 48 - ns.Notes.MAX_NOTES * 20 - 12
  label(f, "Start a timer (TIMER tab; the bar flashes when it ends)", "GameFontNormal"):SetPoint("TOPLEFT", f, "TOPLEFT", 16, y)
  local length = input(f, 56, 8)
  length:SetPoint("TOPLEFT", f, "TOPLEFT", 22, y - 20)
  length:SetText("10m")
  local name = input(f, 170, ns.Notes.MAX_LABEL_BYTES)
  name:SetPoint("LEFT", length, "RIGHT", 10, 0)
  local function start()
    local t, why = ns.Notes.startTimer(settings(), ns.Notes.parseDuration(length:GetText()), name:GetText())
    if t then name:SetText(""); name:ClearFocus(); length:ClearFocus() else say(why) end
    changed()
  end
  length:SetScript("OnEnterPressed", start)
  name:SetScript("OnEnterPressed", start)
  button(f, "Start", 60, start):SetPoint("LEFT", name, "RIGHT", 6, 0)

  for i = 1, ns.Notes.MAX_TIMERS do
    local row = { text = label(f, "") }
    row.text:SetPoint("TOPLEFT", f, "TOPLEFT", 20, y - 48 - (i - 1) * 20)
    pcall(row.text.SetWidth, row.text, W.WIDTH - 80)
    timerRows[i] = row
  end

  local hint = label(f, "Remove them on the Touch Bar (tap the x) or from the tray icon.", "GameFontDisable")
  hint:SetPoint("BOTTOMLEFT", f, "BOTTOMLEFT", 16, 12)

  local since = 0
  f:SetScript("OnUpdate", function(_, elapsed) -- the countdowns, twice a second while the window is open
    since = since + elapsed
    if since >= 0.5 then since = 0; pcall(W.refresh) end
  end)
  f:SetScript("OnShow", function() pcall(W.refresh) end)
  f:Hide()
  frame = f
  return f
end

--- Updates the rows from the saved lists.
function W.refresh()
  if not frame then return end
  local s = settings()
  local list = ns.Notes.read(s)
  for i, row in ipairs(noteRows) do
    local n = list.notes[i]
    row.text:SetText(n and ("sent: " .. n.text) or "")

  end
  local at = tonumber(time()) or 0
  for i, row in ipairs(timerRows) do
    local t = list.timers[i]
    if t then
      local left = t.ends - at
      row.text:SetText(("sent: %s  -  %s"):format(t.label, left > 0 and ns.Notes.formatSeconds(left) or "done"))
    else
      row.text:SetText("")
    end
  end
end

--- Opens or closes the window. Returns true when it is now open (false: couldn't be built - use the commands).
function W.toggle()
  local ok, f = pcall(build)
  if not ok or not f then
    if ns.State then ns.State.recordError("NotesWindow", f) end
    return false
  end
  if f:IsShown() then f:Hide() return true end
  f:Show()
  return true
end
