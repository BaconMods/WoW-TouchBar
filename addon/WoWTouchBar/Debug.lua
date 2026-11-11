-- WoWTouchBar: client probe (Phase 1 step 1). READ-ONLY.
-- Records which client this is, which TOC loaded, and which API names / events exist, so that
-- the real modules are built on facts. Names below are *candidates to check*, not assumed to exist.
-- Saved to WoWTouchBarDB.probe (written to disk on /reload or logout). Runs once per login + /wtbdebug.
local addonName, ns = ...
local Debug = {}
ns.Debug = Debug
Debug.VERSION = 6 -- bump on every probe change; shows in chat and SavedVariables so stale code is obvious

Debug.API_NAMES = {
  -- player / XP
  "UnitLevel", "UnitXP", "UnitXPMax", "GetXPExhaustion", "GetRestState", "IsXPUserDisabled",
  "GetMaxPlayerLevel", "GetMaxLevelForPlayerExpansion", "UnitEffectiveLevel",
  -- quests (modern namespaces and older globals)
  "C_SuperTrack.GetSuperTrackedQuestID", "C_QuestLog.GetSelectedQuest", "C_QuestLog.GetTitleForQuestID",
  "C_QuestLog.GetQuestObjectives", "C_QuestLog.GetNumQuestWatches", "C_QuestLog.GetQuestIDForQuestWatchIndex",
  "C_QuestLog.GetNumQuestLogEntries", "C_QuestLog.GetInfo", "C_QuestLog.GetLogIndexForQuestID",
  "C_QuestLog.IsComplete", "C_QuestLog.ReadyForTurnIn", "C_QuestLog.IsOnQuest",
  "GetNumQuestWatches", "GetQuestIndexForWatch", "GetNumQuestLogEntries", "GetQuestLogTitle",
  "GetNumQuestLeaderBoards", "GetQuestLogLeaderBoard", "GetQuestLogSelection", "IsQuestWatched",
  -- utility
  "GetMoney", "C_Container.GetContainerNumFreeSlots", "C_Container.GetContainerNumSlots",
  "GetContainerNumFreeSlots", "GetContainerNumSlots", "GetInventoryItemDurability",
  -- map / navigation (not MVP)
  "C_Map.GetBestMapForUnit", "C_Map.GetPlayerMapPosition", "C_Map.GetUserWaypoint",
  "C_QuestLog.GetNextWaypoint", "C_Navigation.GetDistance",
  -- addon metadata / display
  "C_AddOns.GetAddOnMetadata", "GetAddOnMetadata", "GetPhysicalScreenSize", "GetScreenWidth",
  "GetScreenHeight", "GetCVar", "GetBuildInfo", "issecretvalue",
  -- Phase 4 candidates (extras): mail, ready check, dungeon finder, reputation, options panel, chat helpers
  "HasNewMail", "GetReadyCheckTimeLeft", "GetReadyCheckStatus",
  "GetLFGMode", "GetLFGQueueStats", "GetLFGProposal", "GetLFGRoles", "GetLFGQueuedList", "GetBattlefieldStatus",
  "C_LFGList.GetActiveEntryInfo",
  "C_Reputation.GetWatchedFactionData", "GetWatchedFactionInfo", "C_Reputation.GetNumFactions", "GetNumFactions",
  "C_Reputation.IsMajorFaction", "C_Reputation.GetFactionParagonInfo", "GetFriendshipReputation",
  "C_GossipInfo.GetFriendshipReputation",
  "Settings.RegisterCanvasLayoutCategory", "Settings.RegisterVerticalLayoutCategory", "Settings.RegisterAddOnCategory",
  "Settings.RegisterProxySetting", "Settings.CreateCheckbox", "Settings.CreateDropdown", "Settings.OpenToCategory",
  "InterfaceOptions_AddCategory", "InterfaceOptionsFrame_OpenToCategory",
  "UnitName", "Ambiguate", "C_ChatInfo.GetChannelInfoFromIdentifier",
}

Debug.EVENTS = {
  "PLAYER_XP_UPDATE", "PLAYER_LEVEL_UP", "UPDATE_EXHAUSTION", "PLAYER_UPDATE_RESTING",
  "QUEST_LOG_UPDATE", "QUEST_WATCH_UPDATE", "QUEST_WATCH_LIST_CHANGED", "QUEST_TURNED_IN",
  "QUEST_ACCEPTED", "QUEST_REMOVED", "SUPER_TRACKING_CHANGED", "UNIT_QUEST_LOG_CHANGED",
  "PLAYER_MONEY", "BAG_UPDATE", "BAG_UPDATE_DELAYED", "UPDATE_INVENTORY_DURABILITY",
  "UI_SCALE_CHANGED", "DISPLAY_SIZE_CHANGED", "ZONE_CHANGED_NEW_AREA",
  -- Phase 4 candidates. Chat events are only registered here to see if they exist; no message is ever read or saved.
  "UPDATE_PENDING_MAIL", "READY_CHECK", "READY_CHECK_FINISHED",
  "LFG_UPDATE", "LFG_QUEUE_STATUS_UPDATE", "LFG_PROPOSAL_SHOW", "LFG_PROPOSAL_FAILED", "LFG_PROPOSAL_SUCCEEDED",
  "UPDATE_BATTLEFIELD_STATUS", "UPDATE_FACTION", "QUEST_COMPLETE",
  "CHAT_MSG_WHISPER", "CHAT_MSG_BN_WHISPER", "CHAT_MSG_GUILD",
  "PLAYER_REGEN_DISABLED", "PLAYER_REGEN_ENABLED",
}

local CVARS = { "uiScale", "useUiScale", "renderScale", "gxWindowedResolution", "gxFullscreenResolution", "gxMaximize" }

--- Resolves "A.B" against the global table without erroring.
function Debug.lookup(name)
  local value = _G
  for part in name:gmatch("[^%.]+") do
    if type(value) ~= "table" then return nil end
    value = value[part]
  end
  return value
end

local function try(fn, ...)
  if type(fn) ~= "function" then return { missing = true } end
  local results = { pcall(fn, ...) }
  if not results[1] then return { error = tostring(results[2]) } end
  local out = {}
  for i = 2, #results do
    local v = results[i]
    out[i - 1] = (type(v) == "table") and "<table>" or v
  end
  return out
end

local function probeApis()
  local apis = {}
  for _, name in ipairs(Debug.API_NAMES) do apis[name] = type(Debug.lookup(name)) end
  return apis
end

local function probeEvents()
  local frame = CreateFrame("Frame")
  local events = {}
  for _, e in ipairs(Debug.EVENTS) do
    local ok = pcall(frame.RegisterEvent, frame, e)
    events[e] = ok and "ok" or "unknown"
  end
  pcall(frame.UnregisterAllEvents, frame)
  return events
end

local function probeDisplay()
  local d = { cvars = {} }
  d.physical = try(GetPhysicalScreenSize)
  d.screen = { try(GetScreenWidth)[1], try(GetScreenHeight)[1] }
  if UIParent then
    d.uiParentScale = try(UIParent.GetEffectiveScale, UIParent)[1]
    d.uiParentSize = { try(UIParent.GetWidth, UIParent)[1], try(UIParent.GetHeight, UIParent)[1] }
  end
  local strip = _G.WoWTouchBarStrip
  if strip then
    d.stripScale = try(strip.GetScale, strip)[1]
    d.stripEffectiveScale = try(strip.GetEffectiveScale, strip)[1]
  end
  for _, name in ipairs(CVARS) do d.cvars[name] = try(GetCVar, name)[1] end
  return d
end

local function probeValues()
  local q = C_QuestLog or {}
  return {
    level = try(UnitLevel, "player"),
    xp = try(UnitXP, "player"),
    xpMax = try(UnitXPMax, "player"),
    exhaustion = try(GetXPExhaustion),
    restState = try(GetRestState),
    maxPlayerLevel = try(GetMaxPlayerLevel),
    money = try(GetMoney),
    superTracked = try(C_SuperTrack and C_SuperTrack.GetSuperTrackedQuestID),
    selectedQuest = try(q.GetSelectedQuest),
    numWatchesNew = try(q.GetNumQuestWatches),
    numWatchesOld = try(GetNumQuestWatches),
    numLogNew = try(q.GetNumQuestLogEntries),
    numLogOld = try(GetNumQuestLogEntries),
  }
end

--- Copies a table up to `depth` levels so return shapes can be inspected in SavedVariables.
function Debug.dump(value, depth)
  if type(value) ~= "table" then
    if type(value) == "function" or type(value) == "userdata" then return "<" .. type(value) .. ">" end
    return value
  end
  if (depth or 0) <= 0 then return "<table>" end
  local out, n = {}, 0
  for k, v in pairs(value) do
    n = n + 1
    if n > 40 then out["<truncated>"] = true; break end
    out[k] = Debug.dump(v, depth - 1)
  end
  return out
end

local function call(fn, ...)
  if type(fn) ~= "function" then return { missing = true } end
  local results = { pcall(fn, ...) }
  if not results[1] then return { error = tostring(results[2]) } end
  local out = {}
  for i = 2, #results do out[i - 1] = Debug.dump(results[i], 3) end
  return out
end

-- Real return shapes for quests / bags / durability (first few entries only).
local function probeShapes()
  local q = C_QuestLog or {}
  local shapes = { watched = {}, superTracked = call(C_SuperTrack and C_SuperTrack.GetSuperTrackedQuestID) }
  local n = (type(q.GetNumQuestWatches) == "function" and select(2, pcall(q.GetNumQuestWatches))) or 0
  if type(n) ~= "number" then n = 0 end
  for i = 1, math.min(n, 3) do
    local id = call(q.GetQuestIDForQuestWatchIndex, i)[1]
    local entry = { index = i, questID = id }
    if type(id) == "number" then
      entry.title = call(q.GetTitleForQuestID, id)
      entry.objectives = call(q.GetQuestObjectives, id)
      entry.logIndex = call(q.GetLogIndexForQuestID, id)
      entry.isComplete = call(q.IsComplete, id)
      entry.readyForTurnIn = call(q.ReadyForTurnIn, id)
      local logIndex = entry.logIndex[1]
      if type(logIndex) == "number" then
        entry.info = call(q.GetInfo, logIndex)
        entry.leaderBoards = call(GetNumQuestLeaderBoards, logIndex)
        entry.leaderBoard1 = call(GetQuestLogLeaderBoard, 1, logIndex)
      end
    end
    shapes.watched[i] = entry
  end
  local c = C_Container or {}
  shapes.bags = {}
  for bag = 0, 4 do
    shapes.bags[bag + 1] = { free = call(c.GetContainerNumFreeSlots, bag), slots = call(c.GetContainerNumSlots, bag) }
  end
  shapes.durability = {}
  for _, slot in ipairs({ 1, 3, 5, 16 }) do shapes.durability[#shapes.durability + 1] = { slot = slot, value = call(GetInventoryItemDurability, slot) } end
  -- Phase 4: return shapes for the extras (faction names are game data, not personal; no chat is read)
  local r = C_Reputation or {}
  shapes.extras = {
    hasNewMail = call(HasNewMail),
    readyCheckTimeLeft = call(GetReadyCheckTimeLeft),
    lfgMode = call(GetLFGMode, 1),            -- 1 = dungeon finder category (to verify)
    lfgQueueStats = call(GetLFGQueueStats, 1),
    lfgRoles = call(GetLFGRoles),
    battlefield1 = call(GetBattlefieldStatus, 1),
    watchedFaction = call(r.GetWatchedFactionData),
    watchedFactionOld = call(GetWatchedFactionInfo),
    numFactions = call(r.GetNumFactions),
  }
  return shapes
end

local function tocVariant()
  local meta = (C_AddOns and C_AddOns.GetAddOnMetadata) or GetAddOnMetadata
  return {
    toc = try(meta, addonName, "X-Toc")[1],
    interface = try(meta, addonName, "Interface")[1],
    version = try(meta, addonName, "Version")[1],
  }
end

-- ---------------------------------------------------------------- combat probe (Phase 4)
-- Answers one question: which combat values can an addon *read* on this client while fighting, and which does
-- the game mark secret? Records only the KIND of each value ("number", "secret", "nil", "table", "missing",
-- "error") - never the values themselves. Nothing is sent anywhere; secret values are never worked around.
local function kind(v)
  if v == nil then return "nil" end
  local check = rawget(_G, "issecretvalue")
  if type(check) == "function" then
    local ok, secret = pcall(check, v)
    if ok and secret then return "secret" end
  end
  return type(v)
end

local function kinds(name, ...)
  local fn = Debug.lookup(name)
  if type(fn) ~= "function" then return "missing" end
  local r = { n = 0 }
  local function keep(ok, ...) r.ok, r.n = ok, select("#", ...); for i = 1, r.n do r[i] = (select(i, ...)) end end
  keep(pcall(fn, ...))
  if not r.ok then return "error" end
  local out = {}
  for i = 1, math.min(r.n, 4) do out[i] = kind(r[i]) end
  return table.concat(out, ","), r[1]
end

local function fieldKinds(tbl, fields)
  if type(tbl) ~= "table" or kind(tbl) == "secret" then return kind(tbl) end
  local out = {}
  for _, f in ipairs(fields) do out[#out + 1] = f .. "=" .. kind(tbl[f]) end
  return table.concat(out, " ")
end

Debug.COMBAT_GCD_SPELL = 61304 -- the global cooldown's spell id (a cooldown every character has)

function Debug.combatSample()
  local s = { time = time() }
  s.inCombat = kinds("UnitAffectingCombat", "player")
  s.playerHealth = kinds("UnitHealth", "player")
  s.playerHealthMax = kinds("UnitHealthMax", "player")
  s.playerPower = kinds("UnitPower", "player")
  s.targetExists = kinds("UnitExists", "target")
  s.targetHealth = kinds("UnitHealth", "target")
  s.targetHealthMax = kinds("UnitHealthMax", "target")
  local k, buff = kinds("C_UnitAuras.GetAuraDataByIndex", "player", 1, "HELPFUL")
  s.playerBuff = k .. " " .. fieldKinds(buff, { "name", "duration", "expirationTime", "spellId" })
  local k2, debuff = kinds("C_UnitAuras.GetAuraDataByIndex", "target", 1, "HARMFUL")
  s.targetDebuff = k2 .. " " .. fieldKinds(debuff, { "name", "duration", "expirationTime", "spellId" })
  local k3, cd = kinds("C_Spell.GetSpellCooldown", Debug.COMBAT_GCD_SPELL)
  s.spellCooldown = k3 .. " " .. fieldKinds(cd, { "startTime", "duration", "isEnabled" })
  return s
end

local combatFrame
function Debug.watchCombat()
  if combatFrame or type(CreateFrame) ~= "function" then return end
  combatFrame = CreateFrame("Frame")
  if not pcall(combatFrame.RegisterEvent, combatFrame, "PLAYER_REGEN_DISABLED") then return end
  combatFrame:SetScript("OnEvent", function()
    C_Timer.After(2, function() -- a moment into the fight, so a target and auras exist
      local ok, sample = pcall(Debug.combatSample)
      WoWTouchBarDB = WoWTouchBarDB or {}
      WoWTouchBarDB.combatProbe = ok and sample or { error = tostring(sample) }
    end)
  end)
end

-- ---------------------------------------------------------------- game UI frames (probe v5)
-- Candidate names of Blizzard's own quest tracker / XP / reputation bars, for the "hide the game's ..." options.
-- Records only whether each exists, is shown, and is protected (protected frames can't be hidden in combat).
Debug.UI_FRAMES = {
  "ObjectiveTrackerFrame", "QuestObjectiveTracker", "ObjectiveTrackerBlocksFrame",
  "StatusTrackingBarManager", "MainStatusTrackingBarContainer", "SecondaryStatusTrackingBarContainer",
  "MainMenuBar", "MainActionBar", "MicroMenuContainer", "EditModeManagerFrame",
  -- probe v6: chat (for "hide the chat window except while typing")
  "ChatFrame1", "ChatFrame1EditBox", "ChatFrame1Tab", "ChatFrame1ButtonFrame", "GeneralDockManager",
  "ChatFrameMenuButton", "ChatFrameChannelButton", "QuickJoinToastButton", "ChatFrame2", "ChatFrame2Tab",
}

function Debug.uiFrames()
  local out = { NUM_CHAT_WINDOWS = tostring(rawget(_G, "NUM_CHAT_WINDOWS")) }
  for _, name in ipairs(Debug.UI_FRAMES) do
    local f = rawget(_G, name)
    if type(f) ~= "table" then
      out[name] = "missing"
    else
      local function ask(method)
        local fn = f[method]
        if type(fn) ~= "function" then return "?" end
        local ok, v = pcall(fn, f)
        return ok and tostring(v) or "error"
      end
      local parent = "?"
      if type(f.GetParent) == "function" then
        local ok, p = pcall(f.GetParent, f)
        if ok and type(p) == "table" and type(p.GetName) == "function" then parent = tostring(p:GetName()) end
      end
      out[name] = ("shown=%s protected=%s parent=%s"):format(ask("IsShown"), ask("IsProtected"), parent)
    end
  end
  return out
end

function Debug.run()
  local build = try(GetBuildInfo)
  local probe = {
    probeVersion = Debug.VERSION,
    time = time(),
    build = build,
    project = { id = _G.WOW_PROJECT_ID, mainline = _G.WOW_PROJECT_MAINLINE, classic = _G.WOW_PROJECT_CLASSIC },
    addon = tocVariant(),
  }
  for key, fn in pairs({ apis = probeApis, events = probeEvents, display = probeDisplay, values = probeValues, shapes = probeShapes, uiFrames = Debug.uiFrames }) do
    local ok, result = pcall(fn)
    probe[key] = ok and result or { error = tostring(result) }
  end
  WoWTouchBarDB = WoWTouchBarDB or {}
  WoWTouchBarDB.probe = probe
  return probe
end

SLASH_WOWTOUCHBARDEBUG1 = "/wtbdebug"
SlashCmdList.WOWTOUCHBARDEBUG = function()
  local p = Debug.run()
  local missing = 0
  for _, kind in pairs(p.apis or {}) do if kind == "nil" then missing = missing + 1 end end
  local watched = (type(p.shapes) == "table" and type(p.shapes.watched) == "table") and #p.shapes.watched or "?"
  print(("|cff8c3cdcWoWTouchBar|r probe v%d saved: build %s (%s), TOC %s, %d API names missing, %s tracked quests sampled. Type /reload to write it to disk."):format(
    Debug.VERSION, tostring(p.build[1]), tostring(p.build[4]), tostring(p.addon.toc), missing, tostring(watched)))
end
