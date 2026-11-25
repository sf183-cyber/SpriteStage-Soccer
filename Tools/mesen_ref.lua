local OUT = "/tmp/soccer_ref"      -- 输出目录（先建好）；Mesen 需在设置里允许脚本访问文件
local N = 1500
local frame = 0
local M = emu.memType
local trace = io.open(OUT.."/ram.bin","wb")
local function shot(name)
  local f = io.open(OUT.."/"..name..".png","wb") f:write(emu.takeScreenshot()) f:close()
end
emu.addEventCallback(function()
  frame = frame + 1
  for base = 0, 0x7FF, 256 do
    local t = {}
    for i = 0, 255 do t[i+1] = emu.read(base + i, M.nesMemory) end
    trace:write(string.char(table.unpack(t)))
  end
  if frame % 60 == 0 then shot(string.format("f%05d", frame)) end
  if frame >= N then trace:close() emu.stop(0) end
end, emu.eventType.endFrame)
emu.addEventCallback(function()
  local t = {}
  if (frame % 40) < 4 then t.start = true end
  if frame > 640 then
    local ph = math.floor(frame/90) % 6
    if ph == 0 then t.right = true elseif ph == 1 then t.up = true t.right = true elseif ph == 2 then t.left = true elseif ph == 3 then t.down = true t.left = true elseif ph == 4 then t.down = true t.right = true else t.up = true end
    if (frame % 50) < 3 then t.a = true end
    if (frame % 77) < 3 then t.b = true end
  end
  emu.setInput(t, 0)
end, emu.eventType.inputPolled)
