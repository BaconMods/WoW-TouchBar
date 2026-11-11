-- WoWTouchBar: notes and timers for the Touch Bar (Phase 7).
-- In game you only ADD them (/wtb note, /wtb timer, the small window). Each gets a unique id and goes into a short
-- "sent" list in SavedVariables, which is re-sent on change and in keyframes (NOTES / TIMERS). The app takes each id
-- in once and owns it from then on, so removing it on the Touch Bar or in the tray deletes it for real and it never
-- comes back - with no time limit, and nothing is lost if the app wasn't running when you added it.
-- (The app can't send anything back into WoW, so the game can't delete what the app already has.)
-- Read-only: "/wtb note here" only reads where you are on the map.
local _, ns = ...
local Compat, Text = ns.Compat, ns.Text
local Notes = {}
ns.Notes = Notes

Notes.MAX_NOTES = 8           -- kept in the "sent" list (the oldest goes first)
Notes.MAX_NOTE_BYTES = 80
Notes.MAX_TIMERS = 8
Notes.MAX_LABEL_BYTES = 32
Notes.MAX_TIMER_SECONDS = 24 * 3600
Notes.KEEP_FINISHED = 3600    -- a timer is still re-sent for an hour after it ends (the app may start late)

local function now() return Compat.number(Compat.call("time")) or 0 end

--- A new id: unique per item (clock seconds * 1000 + a counter), fits in 4 bytes.
local function newId(s, at)
  s.padSeq = ((tonumber(s.padSeq) or 0) + 1) % 1000
  return ((at or now()) * 1000 + s.padSeq) % 4294967295
end

--- The "sent" lists; older saved notes (plain text) get ids so they still reach the app.
local function lists(s)
  if type(s.notes) ~= "table" then s.notes = {} end
  if type(s.timers) ~= "table" then s.timers = {} end
  for i, n in ipairs(s.notes) do
    if type(n) == "string" then s.notes[i] = { id = newId(s), text = n } end
  end
  return s.notes, s.timers
end
--- Seconds for "90", "90s", "10m", "1h30m", "1h 30m", "1:30" (min:sec), "1:02:03" (h:min:sec); a plain number
--- means minutes. nil when it can't be read or is outside 1 s .. 24 h.
function Notes.parseDuration(str)
  if type(str) ~= "string" then return nil end
  local s = str:lower():gsub("^%s+", ""):gsub("%s+$", "")
  local seconds
  local h, m, sec = s:match("^(%d+):(%d%d):(%d%d)$")
  if h then
    seconds = tonumber(h) * 3600 + tonumber(m) * 60 + tonumber(sec)
  else
    m, sec = s:match("^(%d+):(%d%d)$")
    if m then
      seconds = tonumber(m) * 60 + tonumber(sec)
    elseif s:match("^%d+$") then
      seconds = tonumber(s) * 60
    elseif s ~= "" then
      seconds = 0
      local rest = s:gsub("(%d+)%s*([hms])", function(n, unit)
        seconds = seconds + tonumber(n) * ({ h = 3600, m = 60, s = 1 })[unit]
        return ""
      end)
      if rest:match("%S") then return nil end
    end
  end
  if not seconds or seconds < 1 or seconds > Notes.MAX_TIMER_SECONDS then return nil end
  return seconds
end

--- "1:05:00", "4:30", "0:09" (pure).
function Notes.formatSeconds(n)
  n = math.max(0, math.floor(n or 0))
  local h, m, s = math.floor(n / 3600), math.floor(n / 60) % 60, n % 60
  if h > 0 then return ("%d:%02d:%02d"):format(h, m, s) end
  return ("%d:%02d"):format(m, s)
end

--- Adds a note (sent to the bar). Returns true, or false + reason. Past MAX_NOTES the oldest sent one is dropped here
--- (the app has long had it).
function Notes.addNote(s, text, at)
  local notes = lists(s)
  text = Text.truncateBytes(Text.clean(text or ""), Notes.MAX_NOTE_BYTES)
  if text == "" then return false, "the note is empty" end
  notes[#notes + 1] = { id = newId(s, at), text = text }
  while #notes > Notes.MAX_NOTES do table.remove(notes, 1) end
  return true
end

--- Starts a timer of `seconds` (sent to the bar). Returns the timer, or nil + reason.
function Notes.startTimer(s, seconds, label, at)
  local _, timers = lists(s)
  if not seconds or seconds < 1 or seconds > Notes.MAX_TIMER_SECONDS then return nil, "use a time like 90s, 10m, 1h30m or 1:30 (up to 24h)" end
  at = at or now()
  Notes.prune(s, at)
  label = Text.truncateBytes(Text.clean(label or ""), Notes.MAX_LABEL_BYTES)
  if label == "" then label = "Timer" end
  local t = { id = newId(s, at), label = label, ends = at + seconds, total = seconds }
  timers[#timers + 1] = t
  while #timers > Notes.MAX_TIMERS do table.remove(timers, 1) end
  return t
end

--- Stops re-sending timers that ended over KEEP_FINISHED seconds ago.
function Notes.prune(s, at)
  local _, timers = lists(s)
  at = at or now()
  for i = #timers, 1, -1 do
    local t = timers[i]
    if type(t) ~= "table" or type(t.ends) ~= "number" or t.ends < at - Notes.KEEP_FINISHED then table.remove(timers, i) end
  end
end
--- Zone and map coordinates, e.g. "Mulgore 45.2, 63.1", or nil + reason (no map position in instances).
function Notes.here()
  local map = Compat.number(Compat.call("C_Map.GetBestMapForUnit", "player"))
  local zone
  if map then
    local info = Compat.call("C_Map.GetMapInfo", map)
    zone = type(info) == "table" and Compat.plain(info.name) or nil
  end
  zone = zone or Compat.call("GetZoneText")
  local pos = map and Compat.call("C_Map.GetPlayerMapPosition", map, "player")
  local x, y
  if type(pos) == "table" and type(pos.GetXY) == "function" then
    local ok, px, py = pcall(pos.GetXY, pos)
    if ok then x, y = Compat.number(px), Compat.number(py) end
  end
  if not x or not y then return nil, "no map position here (e.g. inside a dungeon)" end
  local where = ("%.1f, %.1f"):format(x * 100, y * 100)
  if type(zone) == "string" and zone ~= "" then where = zone .. " " .. where end
  return where
end

--- State module: what goes on the strip (copies of the "sent" lists).
function Notes.read(settings)
  Notes.prune(settings)
  local notes, timers = lists(settings)
  local out = { notes = {}, timers = {} }
  for i, n in ipairs(notes) do out.notes[i] = { id = n.id, text = n.text } end
  for i, t in ipairs(timers) do out.timers[i] = { id = t.id, label = t.label, ends = t.ends, total = t.total } end
  return out
end

local REMOVE_HINT = "remove notes and timers on the Touch Bar (tap the x) or from the tray icon - that's the one list"

--- Handles "/wtb note ..." and "/wtb timer ..." (`rest` keeps its case). Returns lines to print.
function Notes.command(s, cmd, rest, at)
  at = at or now()
  rest = (rest or ""):gsub("^%s+", ""):gsub("%s+$", "")
  local lower = rest:lower()
  if lower:match("^del") or lower:match("^remove") or lower == "clear" or lower:match("^stop") then return { REMOVE_HINT } end
  if cmd == "note" then
    if rest == "" then return { "usage: /wtb note <text>   /wtb note here [text] (adds where you are)" } end
    local text = rest
    if lower == "here" or lower:match("^here%s") then
      local where, why = Notes.here()
      if not where then return { why } end
      text = where .. (rest:sub(5):match("%S") and (" " .. rest:sub(6)) or "")
    end
    local ok, why = Notes.addNote(s, text, at)
    if not ok then return { why } end
    local notes = lists(s)
    return { "note sent to the Touch Bar: " .. notes[#notes].text }
  elseif cmd == "timer" then
    if rest == "" then return { "usage: /wtb timer <10m | 90s | 1h30m | 1:30> [name]" } end
    local length, label = rest:match("^(%S+)%s*(.*)$")
    local t, why = Notes.startTimer(s, Notes.parseDuration(length), label, at)
    if not t then return { why } end
    return { ("timer sent to the Touch Bar: %s, %s"):format(t.label, Notes.formatSeconds(t.total)) }
  end
  return nil
end

--- Lines for /wtb notes when the window can't open: what was sent recently.
function Notes.list(s, at)
  at = at or now()
  Notes.prune(s, at)
  local notes, timers = lists(s)
  local out = { "sent to the Touch Bar recently (" .. REMOVE_HINT .. "):" }
  for _, n in ipairs(notes) do out[#out + 1] = "  note: " .. n.text end
  for _, t in ipairs(timers) do
    local left = t.ends - at
    out[#out + 1] = ("  timer: %s - %s"):format(t.label, left > 0 and Notes.formatSeconds(left) .. " left" or "done")
  end
  if #notes + #timers == 0 then out[#out + 1] = "  nothing yet - /wtb note <text>, /wtb timer 10m [name]" end
  return out
end