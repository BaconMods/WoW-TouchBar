-- WoWTouchBar: which quest to show, and its objectives (read-only).
-- Selection policy (first match wins): pinned → super-tracked → selected in log → first watched → none.
-- Shapes verified in Forever 1.60.1 (docs/04_FEASIBILITY.md G6, G7).
local _, ns = ...
local Compat, Text = ns.Compat, ns.Text
local Quests = {}
ns.Quests = Quests

Quests.POLICIES = {
  auto = { "pinned", "supertracked", "selected", "watched" },
  watched = { "pinned", "watched" },
}

local function validId(id)
  id = Compat.number(id)
  if id and id > 0 then return id end
end

local function onQuest(id)
  -- If IsOnQuest is unavailable, trust the source rather than hiding everything.
  if not Compat.has("C_QuestLog.IsOnQuest") then return true end
  return Compat.call("C_QuestLog.IsOnQuest", id) == true
end

local function watchedIds()
  local ids = {}
  local n = Compat.number(Compat.call("C_QuestLog.GetNumQuestWatches")) or 0
  for i = 1, n do
    local id = validId(Compat.call("C_QuestLog.GetQuestIDForQuestWatchIndex", i))
    if id then ids[#ids + 1] = id end
  end
  return ids
end

local sources = {
  pinned = function(settings) return validId(settings and settings.pinnedQuestID) end,
  supertracked = function() return validId(Compat.call("C_SuperTrack.GetSuperTrackedQuestID")) end,
  selected = function() return validId(Compat.call("C_QuestLog.GetSelectedQuest")) end,
  watched = function() return watchedIds()[1] end,
}

--- Returns questID, source name — or nil when nothing applies.
function Quests.select(settings)
  local policy = Quests.POLICIES[(settings and settings.questPolicy) or "auto"] or Quests.POLICIES.auto
  for _, name in ipairs(policy) do
    local id = sources[name](settings)
    if id and onQuest(id) then return id, name end
  end
  return nil
end

--- One objective from either API shape → { label, text, type, done, have, need, numeric }.
function Quests.normaliseObjective(text, kind, done, have, need)
  text = type(text) == "string" and text or ""
  have, need = Compat.number(have), Compat.number(need)
  if (have == nil or need == nil) and text ~= "" then
    -- leader-board fallback only gives text: "3/8 Wolf" or "Wolf slain: 3/8"
    local a, b = text:match("(%d+)%s*/%s*(%d+)")
    have, need = tonumber(a), tonumber(b)
  end
  local numeric = kind ~= "log" and need ~= nil and need > 0
  return {
    label = numeric and Text.objectiveLabel(text) or text,
    text = text,
    type = kind,
    done = done == true or (numeric and have ~= nil and have >= need) or false,
    have = numeric and (have or 0) or nil,
    need = numeric and need or nil,
    numeric = numeric,
  }
end

local function readObjectives(id, logIndex)
  local list = {}
  local raw = Compat.call("C_QuestLog.GetQuestObjectives", id)
  if type(raw) == "table" and #raw > 0 then
    for _, o in ipairs(raw) do
      if type(o) == "table" then
        list[#list + 1] = Quests.normaliseObjective(o.text, o.type, o.finished, o.numFulfilled, o.numRequired)
      end
    end
    return list
  end
  if logIndex then
    local n = Compat.number(Compat.call("GetNumQuestLeaderBoards", logIndex)) or 0
    for i = 1, n do
      local text, kind, done = Compat.call("GetQuestLogLeaderBoard", i, logIndex)
      list[#list + 1] = Quests.normaliseObjective(text, kind, done)
    end
  end
  return list
end

--- Full quest state for the selection, or { none = true }.
function Quests.read(settings)
  local id, source = Quests.select(settings)
  local watchedCount = #watchedIds()
  if not id then return { none = true, watchedCount = watchedCount } end

  local logIndex = validId(Compat.call("C_QuestLog.GetLogIndexForQuestID", id))
  local title = Compat.call("C_QuestLog.GetTitleForQuestID", id)
  if type(title) ~= "string" or title == "" then
    local info = logIndex and Compat.call("C_QuestLog.GetInfo", logIndex)
    title = type(info) == "table" and type(info.title) == "string" and info.title or ("Quest " .. id)
  end

  local objectives = readObjectives(id, logIndex)
  local current
  for i, o in ipairs(objectives) do
    if not o.done then current = i; break end
  end
  local ready = Compat.call("C_QuestLog.ReadyForTurnIn", id) == true
  local complete = ready or Compat.call("C_QuestLog.IsComplete", id) == true
  if not complete and #objectives > 0 and not current then complete = true end

  return {
    questID = id,
    source = source,
    title = title,
    objectives = objectives,
    currentObjective = current,
    complete = complete,
    readyForTurnIn = ready,
    watchedCount = watchedCount,
  }
end
