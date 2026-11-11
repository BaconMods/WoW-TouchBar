-- WoWTouchBar: optical protocol v1 (docs/09_PROTOCOL.md). Pure functions: state → bytes → cell grid.
-- Frame (88 bytes, big-endian): "WT", version, type, seq(2), chunkIndex, chunkCount, payloadLen, payload, CRC-16, zero padding.
-- Grid: 89 x 9 cells. Row 0 and column 0 are timing patterns (alternating, white at even index);
-- data bits fill rows 1-8, columns 1-88, row-major, MSB first (11 bytes per row).
local _, ns = ...
local Text = ns.Text
local band, bxor, lshift = bit.band, bit.bxor, bit.lshift

local P = {}
ns.Protocol = P

P.VERSION = 1
P.MAGIC = { 0x57, 0x54 } -- "WT"
P.FRAME_BYTES = 88
P.HEADER_BYTES = 9
P.MAX_PAYLOAD = P.FRAME_BYTES - P.HEADER_BYTES - 2 -- 77
P.COLS, P.ROWS = 89, 9
P.BYTES_PER_ROW = (P.COLS - 1) / 8 -- 11
P.MAX_TEXT = 48
P.TYPE = { HELLO = 1, PLAYER = 2, QUEST = 3, OBJECTIVE = 4, UTILITY = 5, NAVIGATION = 6, HEARTBEAT = 7, STATUS = 8, REPUTATION = 9, CHAT = 10, DISPLAY = 11, NOTES = 12, TIMERS = 13 }
P.FLAVOUR = { unknown = 0, forever = 1, retail = 2 }
P.SOURCE = { pinned = 1, supertracked = 2, selected = 3, watched = 4 }

--- CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF). Check value for "123456789" is 0x29B1.
function P.crc16(bytes, count)
  local crc = 0xFFFF
  for i = 1, count or #bytes do
    crc = bxor(crc, lshift(bytes[i], 8))
    for _ = 1, 8 do
      if band(crc, 0x8000) ~= 0 then
        crc = band(bxor(lshift(crc, 1), 0x1021), 0xFFFF)
      else
        crc = band(lshift(crc, 1), 0xFFFF)
      end
    end
  end
  return crc
end

-- ---------------------------------------------------------------- writer (arithmetic only: no sign issues)
local Writer = {}
Writer.__index = Writer

local function newWriter() return setmetatable({ bytes = {} }, Writer) end

local function clampInt(v, max)
  v = math.floor(v)
  if v < 0 then return 0 end
  if v > max then return max end
  return v
end

function Writer:u8(v, none)
  local b = self.bytes
  b[#b + 1] = (v == nil) and (none or 0xFF) or clampInt(v, 0xFE)
  return self
end

function Writer:raw8(v) local b = self.bytes; b[#b + 1] = v % 256; return self end

function Writer:u16(v)
  v = (v == nil) and 0xFFFF or clampInt(v, 0xFFFE)
  return self:raw8(math.floor(v / 256)):raw8(v)
end

function Writer:u32(v)
  v = (v == nil) and 0xFFFFFFFF or clampInt(v, 0xFFFFFFFE)
  return self:raw8(math.floor(v / 16777216)):raw8(math.floor(v / 65536)):raw8(math.floor(v / 256)):raw8(v)
end

--- 8-byte unsigned (copper fits easily in a double's 53 bits). nil → all 0xFF.
function Writer:u64(v)
  if v == nil then return self:u32(nil):u32(nil) end
  v = math.max(0, math.floor(v))
  local hi, lo = math.floor(v / 4294967296), v % 4294967296
  return self:raw8(math.floor(hi / 16777216)):raw8(math.floor(hi / 65536)):raw8(math.floor(hi / 256)):raw8(hi)
    :raw8(math.floor(lo / 16777216)):raw8(math.floor(lo / 65536)):raw8(math.floor(lo / 256)):raw8(lo)
end

--- Length-prefixed UTF-8, cleaned and cut on a character boundary.
function Writer:str(s, maxBytes)
  s = Text.truncateBytes(Text.clean(s), maxBytes or P.MAX_TEXT)
  self:raw8(#s)
  for i = 1, #s do self:raw8(s:byte(i)) end
  return self
end

-- ---------------------------------------------------------------- message payloads
function P.hello(flavour, interface, version)
  local major, minor, patch = (version or "0.0.0"):match("(%d+)%.(%d+)%.(%d+)")
  return newWriter():raw8(P.FLAVOUR[flavour] or 0):u32(interface)
    :raw8(tonumber(major) or 0):raw8(tonumber(minor) or 0):raw8(tonumber(patch) or 0).bytes
end

function P.player(p)
  local flags = (p.isMaxLevel and 1 or 0) + ((p.xp == nil or p.xpMax == nil) and 2 or 0)
  return newWriter():u8(p.level):u32(p.xp):u32(p.xpMax):u32(p.rested):raw8(flags).bytes
end

function P.quest(q)
  if not q or q.none then
    return newWriter():u32(0):raw8(0):raw8(0):raw8(0):raw8(0xFF):str("").bytes
  end
  local flags = (q.complete and 1 or 0) + (q.readyForTurnIn and 2 or 0)
  return newWriter():u32(q.questID):raw8(P.SOURCE[q.source] or 0):raw8(math.min(#q.objectives, 255)):raw8(flags)
    :u8(q.currentObjective):str(q.title).bytes
end

function P.objective(questID, index, o)
  local flags = (o.done and 1 or 0) + (o.numeric and 2 or 0)
  return newWriter():u32(questID):raw8(index):u16(o.have):u16(o.need):raw8(flags):str(o.label).bytes
end

function P.utility(u)
  return newWriter():u64(u.money):u8(u.bagsFree):u8(u.bagsTotal):u8(u.durabilityLowest).bytes
end

--- STATUS: flags (1 new mail, 2 ready check, 4 dungeon proposal, 8 queued, 16 tank, 32 healer, 64 dps),
--- ready-check seconds left (u8), seconds already queued (u32). The app counts both on by itself.
function P.status(s)
  s = s or {}
  local flags = (s.newMail and 1 or 0) + (s.readyCheck and 2 or 0) + (s.proposal and 4 or 0) + (s.queued and 8 or 0)
    + (s.tank and 16 or 0) + (s.healer and 32 or 0) + (s.dps and 64 or 0)
  return newWriter():raw8(flags):u8(s.readyCheckSeconds):u32(s.queuedSeconds).bytes
end

--- REPUTATION: flags (1 = none watched), faction id (u32), reaction 1-8 (u8), value / max within the standing
--- (u32 each, unknown = sentinel), faction name, standing label.
function P.reputation(r)
  if not r or r.none then
    return newWriter():raw8(1):u32(nil):u8(nil):u32(nil):u32(nil):str(""):str("").bytes
  end
  return newWriter():raw8(0):u32(r.factionID):u8(r.reaction):u32(r.value):u32(r.max):str(r.name):str(r.standing).bytes
end

--- DISPLAY: the player's options - pages shown (1 B: 1 quest, 2 xp, 4 bags, 8 rep), alerts on (1 B: 1 quest
--- complete, 2 bags full, 4 repair, 8 level up, 16 mail, 32 ready check, 64 dungeon ready).
function P.display(d)
  return newWriter():raw8(d.pages % 256):raw8(d.alerts % 256).bytes
end

P.MAX_CHAT_TEXT = 255 -- WoW's own chat limit; long messages go out in several frames (chunking)

--- CHAT: kind (1 whisper, 2 Battle.net whisper, 3 guild, 4 guild mention), id (u16), sender (str, may be
--- empty), text (str up to 255 bytes). Shown on the bar, never stored by addon or app.
function P.chat(m)
  return newWriter():raw8(m.kind):u16(m.id):str(m.sender):str(m.text, P.MAX_CHAT_TEXT).bytes
end

P.MAX_NOTE_TEXT = 80
P.MAX_TIMER_LABEL = 32

--- NOTES: count (u8), then each note: id (u32, unique; the app takes each id in once), text (str up to 80 bytes).
function P.notes(list)
  list = list or {}
  local w = newWriter():raw8(math.min(#list, 255))
  for i = 1, math.min(#list, 255) do w:u32(list[i].id):str(list[i].text, P.MAX_NOTE_TEXT) end
  return w.bytes
end

--- TIMERS: count (u8), then each timer: id (u32, unique; taken in once like NOTES), end time (u32, seconds since 1970 from time()), length in seconds (u32),
--- name (str up to 32 bytes). The whole list every time (Phase 7).
function P.timers(list)
  list = list or {}
  local w = newWriter():raw8(math.min(#list, 255))
  for i = 1, math.min(#list, 255) do
    local t = list[i]
    w:u32(t.id):u32(t.ends):u32(t.total):str(t.label, P.MAX_TIMER_LABEL)
  end
  return w.bytes
end

function P.heartbeat(uptimeSeconds)
  return newWriter():u16(uptimeSeconds % 65535).bytes
end

-- ---------------------------------------------------------------- framing
--- Splits a payload into one or more 88-byte frames sharing `seq`.
function P.frames(msgType, seq, payload)
  local count = math.max(1, math.ceil(#payload / P.MAX_PAYLOAD))
  local out = {}
  for chunk = 0, count - 1 do
    local first = chunk * P.MAX_PAYLOAD + 1
    local last = math.min(#payload, first + P.MAX_PAYLOAD - 1)
    local f = { P.MAGIC[1], P.MAGIC[2], P.VERSION, msgType,
      math.floor(seq / 256) % 256, seq % 256, chunk, count, last - first + 1 }
    for i = first, last do f[#f + 1] = payload[i] end
    local crc = P.crc16(f, #f)
    f[#f + 1] = math.floor(crc / 256)
    f[#f + 1] = crc % 256
    while #f < P.FRAME_BYTES do f[#f + 1] = 0 end
    out[#out + 1] = f
  end
  return out
end

--- Grid shapes that all carry 704 data bits ((cols - 1) * (rows - 1)); the companion detects which one is shown
--- by counting the timing row. Thinner shapes are less visible; the default is the original 89 x 9.
P.LAYOUTS = { [89] = 9, [177] = 5, [353] = 3, [705] = 2 }

--- Cell values (1 = white) for a frame, flat array index = row * cols + col + 1.
function P.toGrid(frame, cols, rows)
  cols, rows = cols or P.COLS, rows or P.ROWS
  local grid = {}
  for r = 0, rows - 1 do
    for c = 0, cols - 1 do
      local v
      if r == 0 then v = (c % 2 == 0) and 1 or 0
      elseif c == 0 then v = (r % 2 == 0) and 1 or 0
      else
        local bitIndex = (r - 1) * (cols - 1) + (c - 1)
        local byte = frame[math.floor(bitIndex / 8) + 1]
        v = math.floor(byte / 2 ^ (7 - bitIndex % 8)) % 2
      end
      grid[r * cols + c + 1] = v
    end
  end
  return grid
end

--- Line layout: a single row of LINE_COLS colour cells, a third the height of the 3-row grid.
--- Green alternates (on at even cells) as the timing pattern; red and blue carry 2 data bits per
--- cell (red = bit 2i, blue = bit 2i+1, MSB first), so 352 cells hold all 704 bits.
P.LINE_COLS = 352

--- Rows for a geometry's column count (1 for the line layout), or nil if unknown.
function P.rowsFor(cols)
  if cols == P.LINE_COLS then return 1 end
  return P.LAYOUTS[cols]
end

local function frameBit(frame, bitIndex)
  local byte = frame[math.floor(bitIndex / 8) + 1]
  return math.floor(byte / 2 ^ (7 - bitIndex % 8)) % 2
end

--- Flat {r, g, b, r, g, b, ...} (0/1) for the line layout.
function P.toLine(frame)
  local out = {}
  for i = 0, P.LINE_COLS - 1 do
    out[i * 3 + 1] = frameBit(frame, 2 * i)
    out[i * 3 + 2] = (i % 2 == 0) and 1 or 0
    out[i * 3 + 3] = frameBit(frame, 2 * i + 1)
  end
  return out
end

function P.hex(bytes)
  local t = {}
  for i = 1, #bytes do t[i] = ("%02X"):format(bytes[i]) end
  return table.concat(t)
end
