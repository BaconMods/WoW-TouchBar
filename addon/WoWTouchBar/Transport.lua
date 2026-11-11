-- WoWTouchBar: draws protocol frames in the visible strip and schedules what to send.
-- The strip stays still unless something changes: changed messages go out promptly, and a full
-- keyframe every KEYFRAME seconds recovers from missed frames. Each frame is held for HOLD seconds.
-- Geometry (cell size, grid shape, brightness) is configurable so the strip can be as small and dim
-- as capture allows; /wtb sizetest cycles through candidates for measuring.
local _, ns = ...
local P, Compat = ns.Protocol, ns.Compat
local T = {}
ns.Transport = T

T.HOLD = 0.2          -- seconds each frame stays on screen
T.KEYFRAME = 30       -- seconds between full re-sends (each takes ~1.4 s of changing pixels)
T.VERSION = "1.1.0"

--- Brightness of "on" cells (bright, dark, dim).
T.TONES = { bright = 1, dark = 0.35, dim = 0.2 }

--- Default geometry: cell size in physical pixels (as the game reports them), columns, white level.
--- cols = P.LINE_COLS is the one-row colour line; other values are the grey grids in P.LAYOUTS.
--- Size tests 2026-11-09 (dev Mac, game 1680x1050 stretched to 2560x1600): grey grids need 2 px cells,
--- but the colour line reads 100 % even at 1 px cells (contrast 139 bright / 68 dark), because only
--- the green channel has to show cell edges. Default: 1 px dark line, ~536 x 1.5 screen px.
T.DEFAULT_GEOMETRY = { cell = 1, cols = P.LINE_COLS, white = 0.35 }

--- Candidates for the size test, smallest first. Each runs for TEST_SECONDS with keyframes looping.
T.TEST_GEOMETRIES = {
  { cell = 1, cols = P.LINE_COLS, white = 1 }, { cell = 1, cols = P.LINE_COLS, white = 0.35 },
  { cell = 1, cols = P.LINE_COLS, white = 0.2 },
  { cell = 2, cols = P.LINE_COLS, white = 0.35 },
  { cell = 2, cols = 353, white = 0.35 }, -- grey grid, for comparison
}
T.TEST_SECONDS = 5

local strip
local pool = {}                     -- textures, reused across geometry changes
local geometry
local queue, pendingByKey = {}, {}  -- messages waiting to be shown (latest payload per key)
local lastSent = {}                 -- key -> payload hex, to send only what changed
local current = {}                  -- frames of the message being shown
local lastFrame                     -- redrawn when the geometry changes
local seq = 0
local ticks, sinceKeyframe = 0, 0   -- whole ticks (summing 0.2 drifts below whole seconds)
local lastState
local test                          -- { index, ticks } while /wtb sizetest runs

local function ticksFor(seconds) return math.floor(seconds / T.HOLD + 0.5) end

local function diag()
  WoWTouchBarDB = WoWTouchBarDB or {}
  WoWTouchBarDB.transport = WoWTouchBarDB.transport or { frames = 0 }
  return WoWTouchBarDB.transport
end

function T.physicalScale()
  local _, h = Compat.call("GetPhysicalScreenSize")
  h = Compat.number(h)
  if h and h > 0 then return 768 / h end
  return 1
end

local function validGeometry(g)
  return type(g) == "table" and type(g.cell) == "number" and g.cell >= 1 and g.cell <= 8
    and P.rowsFor(g.cols) ~= nil and type(g.white) == "number" and g.white > 0 and g.white <= 1
end

function T.geometry() return geometry end

local function draw(frame)
  lastFrame = frame
  local g = geometry
  local w = g.white
  if g.cols == P.LINE_COLS then
    local rgb = P.toLine(frame)
    for i = 1, P.LINE_COLS do
      pool[i]:SetColorTexture(rgb[i * 3 - 2] * w, rgb[i * 3 - 1] * w, rgb[i * 3] * w, 1)
    end
    return
  end
  local grid = P.toGrid(frame, g.cols, P.LAYOUTS[g.cols])
  for i, v in ipairs(grid) do
    local level = v == 1 and w or 0
    pool[i]:SetColorTexture(level, level, level, 1)
  end
end

--- Lays out the cells for geometry g (reusing textures) and redraws the last frame.
function T.applyGeometry(g)
  if not validGeometry(g) then g = T.DEFAULT_GEOMETRY end
  geometry = { cell = g.cell, cols = g.cols, white = g.white }
  if not strip then return end
  local rows = P.rowsFor(g.cols)
  strip:SetSize(g.cols * g.cell, rows * g.cell)
  local n = g.cols * rows
  for r = 0, rows - 1 do
    for c = 0, g.cols - 1 do
      local i = r * g.cols + c + 1
      local t = pool[i]
      if not t then
        t = strip:CreateTexture(nil, "OVERLAY")
        pool[i] = t
      end
      t:ClearAllPoints()
      t:SetSize(g.cell, g.cell)
      t:SetPoint("TOPLEFT", strip, "TOPLEFT", c * g.cell, -r * g.cell)
      t:SetColorTexture(0, 0, 0, 1)
      t:Show()
    end
  end
  for i = n + 1, #pool do pool[i]:Hide() end
  if lastFrame then draw(lastFrame) end
  local d = diag()
  d.geometry = { cell = g.cell, cols = g.cols, white = g.white }
end

function T.build(settingsGeometry)
  if not strip then
    strip = CreateFrame("Frame", "WoWTouchBarStrip", UIParent)
    strip:SetFrameStrata("TOOLTIP")
    strip:SetIgnoreParentScale(true)
    strip:SetScale(T.physicalScale())
    strip:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, 0)
    strip:Show()
  end
  T.applyGeometry(settingsGeometry or geometry or T.DEFAULT_GEOMETRY)
  return strip
end

function T.rescale() if strip then strip:SetScale(T.physicalScale()) end end

--- Queues a message; a newer payload for the same key replaces one still waiting.
function T.enqueue(key, msgType, payload)
  if pendingByKey[key] then
    pendingByKey[key].payload = payload
    return
  end
  local item = { key = key, type = msgType, payload = payload }
  pendingByKey[key] = item
  queue[#queue + 1] = item
  local d = diag()
  if #queue > (d.queueHigh or 0) then d.queueHigh = #queue end
end

local function messagesFor(state)
  local list = {}
  local _, _, _, interface = Compat.call("GetBuildInfo")
  list[#list + 1] = { "hello", P.TYPE.HELLO, P.hello(state.flavour, interface, T.VERSION) }
  if state.player then list[#list + 1] = { "player", P.TYPE.PLAYER, P.player(state.player) } end
  if state.quests then
    list[#list + 1] = { "quest", P.TYPE.QUEST, P.quest(state.quests) }
    if not state.quests.none then
      for i, o in ipairs(state.quests.objectives) do
        list[#list + 1] = { "objective" .. i, P.TYPE.OBJECTIVE, P.objective(state.quests.questID, i, o) }
      end
    end
  end
  if state.utility then list[#list + 1] = { "utility", P.TYPE.UTILITY, P.utility(state.utility) } end
  if state.status then list[#list + 1] = { "status", P.TYPE.STATUS, P.status(state.status) } end
  if state.reputation then list[#list + 1] = { "reputation", P.TYPE.REPUTATION, P.reputation(state.reputation) } end
  if state.display then list[#list + 1] = { "display", P.TYPE.DISPLAY, P.display(state.display) } end
  if state.notes then
    list[#list + 1] = { "notes", P.TYPE.NOTES, P.notes(state.notes.notes) }
    list[#list + 1] = { "timers", P.TYPE.TIMERS, P.timers(state.notes.timers) }
  end
  return list
end

--- Queues one chat message (never re-sent by keyframes). At most Chat.MAX_PENDING wait; older ones are dropped
--- so a busy guild chat can't build a backlog on the strip.
function T.sendChat(m)
  local waiting = {}
  for i, item in ipairs(queue) do if item.type == P.TYPE.CHAT then waiting[#waiting + 1] = i end end
  local max = (ns.Chat and ns.Chat.MAX_PENDING) or 3
  while #waiting >= max do
    local i = table.remove(waiting, 1)
    pendingByKey[queue[i].key] = nil
    table.remove(queue, i)
    for k = 1, #waiting do waiting[k] = waiting[k] - 1 end
  end
  T.enqueue("chat" .. m.id, P.TYPE.CHAT, P.chat(m))
end

--- Sends only messages whose content changed (called after every state collect).
function T.onState(state)
  lastState = state
  for _, m in ipairs(messagesFor(state)) do
    local hex = P.hex(m[3])
    if lastSent[m[1]] ~= hex then
      lastSent[m[1]] = hex
      T.enqueue(m[1], m[2], m[3])
    end
  end
end

function T.keyframe()
  if not lastState then return end
  for _, m in ipairs(messagesFor(lastState)) do T.enqueue(m[1], m[2], m[3]) end
end

--- Starts the size test: each candidate geometry for TEST_SECONDS, then back to `restore`.
function T.startSizeTest(restore)
  test = { index = 1, ticks = 0, restore = restore }
  T.applyGeometry(T.TEST_GEOMETRIES[1])
  local d = diag()
  d.sizeTest = { started = time(), seconds = T.TEST_SECONDS, geometries = T.TEST_GEOMETRIES }
end

function T.testing() return test ~= nil end

local function stepSizeTest()
  test.ticks = test.ticks + 1
  if test.ticks % ticksFor(1.4) == 0 then T.keyframe() end -- keep frames changing so decoding is exercised
  if test.ticks >= ticksFor(T.TEST_SECONDS) then
    test.ticks = 0
    test.index = test.index + 1
    local g = T.TEST_GEOMETRIES[test.index]
    if g then
      T.applyGeometry(g)
    else
      local restore = test.restore
      test = nil
      T.applyGeometry(restore)
      diag().sizeTest.finished = time()
      print("|cff8c3cdcWoWTouchBar|r size test finished.")
    end
  end
end

--- Called every HOLD seconds: show the next chunk or the next queued message; otherwise keep still.
function T.tick()
  if not strip then return end
  ticks = ticks + 1
  sinceKeyframe = sinceKeyframe + 1
  if test then stepSizeTest() end
  if sinceKeyframe >= ticksFor(T.KEYFRAME) then
    sinceKeyframe = 0
    T.keyframe()
  end
  if #current == 0 then
    local item = table.remove(queue, 1)
    if item then
      pendingByKey[item.key] = nil
      seq = (seq + 1) % 65536
      current = P.frames(item.type, seq, item.payload)
    end
  end
  local frame = table.remove(current, 1)
  if frame then
    draw(frame)
    local d = diag()
    d.frames = (d.frames or 0) + 1
    d.seq = seq
    d.lastType = frame[4]
  end
end

function T.queueLength() return #queue end
