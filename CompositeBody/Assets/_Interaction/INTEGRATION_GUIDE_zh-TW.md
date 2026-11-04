# 把 `_Interaction` 的互動整合進 MainScene（給人看的說明）

> 給 AI agent 用的英文版在同一個資料夾：`INTEGRATION_GUIDE_AGENT.md`（內容更細，附檢查清單）。
> 這份的內容根據 2026-11-04 `home-interaction` 分支（commit `c859c824`）的程式和 MainScene 現況整理。

## 一句話說明
把三個做好的互動（Home、ObjectAssembly 檯燈版、ObjectAssembly 相機版）放進正式的 MainScene，讓它們在 show 跑到對應段落時自動開始，並且兩位玩家能連線一起玩。

---

## 1. 手上有什麼

| 互動 | 要放進 MainScene 的 prefab | 測試用 demo 場景 | 內容 |
|---|---|---|---|
| Home | `Home/Prefabs/HomeInteraction.prefab` | `Home/S2_v1.unity` | 5 片紙片拼成小房子，每拼好一片，大房間對應的牆和家具就浮現回來 |
| 檯燈（Lamp） | `ObjectAssembly/Prefabs/OA_Lamp.prefab` | `ObjectAssembly/OA_Lamp_Demo.unity` | 兩人各認領半個檯燈，靠近時出現光線，合體後飄到終點 |
| 相機（Camera） | `ObjectAssembly/Prefabs/OA_Camera.prefab` | `ObjectAssembly/OA_Camera_Demo.unity` | 跟檯燈版同一套流程，換成相機 |

- 三個 prefab 都只包含互動本身，**沒有** NetworkManager、工作人員面板、rig 這類啟動器。MainScene 本來就有，不要再放一份。
- 每個互動結束時都有「完成」事件（`onCompleted`），可以拿來接下一段流程。
- **目前測過的**：在 demo 場景裡，單機自動測試全過；用 Multiplayer Play Mode 跑過雙人測試，Home 和檯燈版通過。
- **還沒測的**：頭盔實機、兩台 PC，以及放進 MainScene 之後的表現。

---

## 2. MainScene 現在的樣子（重點）

- show 由 `/Experience/ExperienceDirector/ShowTimeline` 上的 Timeline（`Main.playable`）驅動。每一段都是打開或關閉一個 `Beat_xxx` 物件。
- S2 的場景內容在 `/Experience/Beats/Beat_S2/Content/S2_v1`，裡面有大房間 `S3/Inner_Wall` 和美術用的 `LowPolyWall`。
- 舊的兩個 S2 互動在 `/Experience/Beats/` 底下：
  - `S2-2 Fragments`（舊版檯燈）
  - `S2-3 Dollhouse`（舊版娃娃屋）
  - 兩個目前都是**關閉**的。
- **大房間有三個地方要處理**：
  - `Inner_Wall` 現在是**關閉**的。
  - 它的牆被舊娃娃屋的工具永久換成了 `*_Dissolve` 材質。
  - 牆上的 Batching Static 還開著，開著的話物件在 play 時動不了。

---

## 3. 開始前要先決定的事

1. **哪個互動放哪一段**
   - 建議：Home 取代 **S2-3**（同一個房間、同一批碎片），檯燈取代 **S2-2**（本來就是兩人拼檯燈）。
   - 相機版**目前沒有位置**，要新增一段（例如 `Beat_S2-4`，需要改 Timeline），或放到別的段落。
2. **舊的 S2-2 / S2-3 要不要刪**：建議先保持關閉，不要刪。
3. **show 要不要等互動完成**
   - 建議要等：在 Timeline 上加一個「暫停點」，互動完成才繼續。
   - 不然就只能把那一段的時間拉到夠長。
4. **檯燈版、相機版的燈**：用 prefab 自帶的 2 盞燈（Phase0 時漸亮），還是改成控制 S2 場景原本的燈。
5. **檯燈、相機在房間裡的位置**：兩個起始角落和最後的終點擺在哪。
6. **旁白音檔**：目前 Phase0/1/2 都是空的，要放正式音檔。

---

## 4. 整合步驟

> 建議把這些步驟寫成工具（例如選單 `Tools/Home/Integrate Into MainScene`），一鍵執行、跑完自動檢查。手動做容易漏，美術一改場景就得重做一次。

### 所有互動都要做
1. **放 prefab**：放在 `/Experience/Beats/` 底下，跟舊的 `S2-2 Fragments` 放在一起。
   - ❌ 不能放在 `Beat_S2-x` 底下。那個物件平常是關閉的，網路物件只在連線同步時、對「當下啟用中」的物件生成一次，關著就永遠不會生成。
   - ❌ 也不要放在 `Playspace` 底下。
2. **接開關**：把互動上的 `Beat Root` 設成那一段的 Beat（例如 Home 設 `Beat_S2-3`）。沒設的話，工作人員一按 Start 互動就開始，時間點不對。
3. **存檔後重新產生網路 ID**：存一次、產生 NetworkObject hash、再存一次。漏了這步，client 會對不上物件，碎片不會同步。

### Home 額外要做
1. **對齊位置**：讓 `HomeInteraction` 跟 MainScene 裡 S2 的 `S3` 房間位置一致。對齊的標準是：Home 的 5 個組裝位置要跟 S2 裡 `LowPolyWall` 美術碎片的位置完全重合，誤差在 0.5 mm 以內。
2. **打開 `Inner_Wall`**：不用擔心房間會提早出現。Home 會先把每件牆和家具隱藏，等對應的碎片黏好才浮現。
3. **把牆的材質換回原本的**：把舊娃娃屋留下的 `*_Dissolve` 材質覆寫還原成原始材質。
   - 這步最容易漏。
   - 不做的話，Home 會把走樣的材質當成「原本的樣子」，浮現完成後外觀就不對。
4. **重新指定 6 組牆**：Floor、Back、Front、Left、Right、Top，每組都要指到 MainScene 裡對應的那面牆。demo 場景用的路徑在這裡對不上。
5. **關掉 Batching Static**：Contribute GI 保留。
6. **關掉 S2 裡的 `LowPolyWall` 美術碎片**：Home 有自己的可抓碎片，兩份同時出現會重複。
7. **時間**：Home 最多 180 秒會自動補完；S2-3 大約 328 秒（以 Timeline 實際長度為準）。

### 檯燈版 / 相機版額外要做
1. **擺位置**：把它擺到那一段玩家所在的位置，兩個角落和終點也一起調整。
2. **燈**：照第 3 節第 4 點的決定處理。
3. **放音檔**：
   - Phase1 每個角色各一段：`Phase1 Clip P1`、`Phase1 Clip P2`。
   - Phase2 一段：`Phase2 Clip`。
   - Phase0 的旁白要放在 `OA_Lamp_Phase0.playable` / `OA_Camera_Phase0.playable` 的 Audio 軌上。
4. **相機版要先有自己的一段 Beat**，再放進去。

### 接上「完成之後」（建議）
1. 加一個小元件，把互動的 `onCompleted` 轉成故事任務完成，例如任務名 `home.done`、`oa.lamp.done`。
2. 在 Timeline 那一段的結尾放一個暫停點（`ShowHoldMarker`），填上同一個任務名。
3. 這樣 show 會停在那裡，等兩位玩家都完成才往下走。單人模式會自動處理。

---

## 5. 怎麼確認整合成功

- [ ] 編譯沒有錯誤。
- [ ] 檢查工具全部 PASS：
  - 三個互動各只有一份；`Beat Root` 都有設；網路 ID 不是 0 也不重複。
  - Home 的組裝位置和美術碎片重合；6 組牆都指到 MainScene。
  - 牆上沒有 `*_Dissolve`、沒有 Batching Static；`Inner_Wall` 是打開的。
  - 場景裡只有一個 NetworkManager。
- [ ] 原本 demo 場景的測試還是全過：Home 72 項、檯燈 45 項、相機 45 項。
- [ ] MainScene 單人 play，跳到 S2 段：互動要在對的時間開始，能完成，show 能接下去。
- [ ] 兩人測試（Multiplayer Play Mode）；最後用兩台 PC + 頭盔實測。

---

## 6. 常見地雷

- **用 Multiplayer Play Mode 測雙人時，兩個視窗的身分會撞**：Player 2 會搶走 host 的 Player1 角色。測試時要給 Player 2 不同的身分資料；真的兩支頭盔不會有這個問題。
- **第二個人還沒拿到角色就按 Start**：會變成單人模式。要等工作人員面板上兩個角色都到齊再按。
- **在 play mode 中重新編譯**：會把網路連線打斷，有時 UDP 7777 會被佔住，要重開 Unity 才會釋放。這台電腦已經把 Unity 偏好設成「播放結束後才重新編譯」。
- **Home 斷線重連的那台機器，「完成」事件會再觸發一次**：後面接的流程要能承受被叫兩次，或在 Home 加防護。
- **play 過 `Home/S2_v1.unity` 之後它會顯示成「已修改」**：那是 rig UI 和水面自己標的，從磁碟重新開啟就好，不要存。
- **Console 裡的兩種錯誤跟這些互動無關**：Vivox 的 `403 Forbidden`，以及 KriptoFX 水面的 NullReference。
- **Timeline、Build Settings、ProjectSettings 是整個團隊共用的**：改之前先確認，並且分開 commit。
