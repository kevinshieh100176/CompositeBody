# Home 人工測試（不用頭盔，滑鼠鍵盤模擬 XR）

測試場景：`Assets/_Interaction/Home/Home_ManualTest.unity`

這個場景是 `S2_v1.unity` 的**複本**，另外加上三樣東西：
- XR Interaction Simulator：用滑鼠鍵盤模擬頭盔和手把。
- Desktop Test HUD：畫面右上角，可以開房、加入、開始，也有 Home 的測試按鈕。
- 雙人連線需要的整套網路元件。

**不要手動改這個場景。** 每次 `S2_v1` 有更新，執行 **Tools > Home > Build Manual Test Scene** 重新產生就好。產生時 `S2_v1` 本身不會被改動。

---

## 單人測試
1. 打開 `Home_ManualTest.unity`，按 Play。
2. 在 HUD 按 **Host (F5)**，再按 **Solo (F8)**。
3. 5 片紙片會散在房間裡、開始呼吸。這時大房間是暗的（尚未浮現）。
4. 用模擬器（操作見下方）走過去抓紙片，拿到中間的組裝區：
   - 拿在手上會開始抖動，約 1.5 秒後自動黏上。
   - 或是在組裝區裡直接放開。
5. 每黏好一片，對應的牆和家具會浮現回來。5 片都完成後紙片消失，天花板最後出現。

**HUD 的 Home 區塊捷徑：**
- **Place next**：直接黏合下一片。
- **Expire timeout**：馬上觸發「時間到」，測試自動補完的流程。
- **Reset**：重來一輪。
- **Teleport me to the assembly zone**：把自己傳送到組裝區前面。

> 這個測試場景的 timeout 設成 30 分鐘，讓你有時間慢慢操作。正式的 `S2_v1` 仍然是 180 秒。

## 雙人測試（Multiplayer Play Mode）
1. 打開 **Window > Multiplayer > Multiplayer Play Mode**，勾選 **Player 2**。
2. 主 editor 打開 `Home_ManualTest.unity`。如果 Player 2 的視窗顯示的是別的場景，在 Player 2 視窗也打開同一個場景。
3. 按 Play，兩個視窗都會進入 play mode。
4. 主 editor：**Host (F5)**。
5. Player 2：**Join (F6)**，預設連到 127.0.0.1:7777。Player 2 會自動使用自己的身分（HUD 上 "Own identity (MPPM clone)" 會打勾），不會跟 host 搶 Player1。
6. 等 HUD 上顯示 `roles: Player1=#0 Player2=#1`，再到主 editor 按 **Start (F7)**。
   - 人沒到齊時 F7 會拒絕開始，避免不小心變成單人模式。
   - 要單人就按 F8。
7. 兩個視窗各自用模擬器抓紙片。可以測：
   - 搶同一片（先抓先贏）
   - client 黏合
   - 放手後另一人接手
   - 一方 Leave (F9) 後重新 Join

**兩台 PC：** host 那台按 F5；另一台在 HUD 的 Join 欄位填 host 的 IP（host 的工作人員面板上會顯示 `Hosting <IP>:7777`），按 F6。另一台的 HUD 要取消勾選 Own identity。

---

## 操作方式

### 模擬器（XR Interaction Simulator）
| 按鍵 | 作用 |
|---|---|
| 按住右鍵 + 移動滑鼠 | 轉頭 |
| WASD | 移動 |
| 左鍵 | 對準的東西 select / 抓（point and click） |
| G | 握（grip） |
| T | 扳機（trigger） |
| Tab | 切換 FPS 模式 / 單手操作模式 |
| `[` / `]` | 切換左手 / 右手 |
| R | 重置位置 |

### HUD
| 按鍵 | 作用 |
|---|---|
| F1 | 鎖定「滑鼠給 UI」 |
| F5 | Host |
| F6 | Join |
| F7 | Start（需要兩人到齊） |
| F8 | Solo |
| F9 | Leave |
| F10 | 收合 HUD |
| F2 | 抓視線中最近的物件（備用的抓取工具） |

### 為什麼 UI 點得到了
模擬器會把滑鼠左鍵當成手把的 select，右鍵拿來轉頭，所以以前在 play mode 裡點不到工作人員面板。現在的處理方式：
- **滑鼠移到 HUD 上**：模擬器的滑鼠輸入會自動暫停，點擊只會送到 UI。
- **按 F1 鎖定**：工作人員面板等其他 UI 也點得到。再按一次 F1 解除。
- **網路操作都有 F 鍵**：完全不需要點擊也能操作。

---

## 疑難排解
- **進 play 時跳出「Add Scene to Scenes in Build」**：這個場景沒在 Build Settings 裡。重新執行 Build Manual Test Scene 會自動加入。
- **Host 失敗（port 被佔）**：在 play 中重新編譯過，UDP 7777 可能被佔住，重開 Unity 即可。這台電腦已設定成「播放結束後才重新編譯」。
- **頭不會動**：
  - 如果 Quest Link 開著，真頭盔和模擬器會互相搶，測試時先關掉 Link。
  - 確認場景裡的 XR Interaction Simulator 是啟用狀態。
- **Player 2 卡在進 play mode**：第一次要編譯和載入，會比較久。如果 Player 2 視窗有對話框，點掉它。
- **Console 會出現一些跟 Home 無關的訊息**：
  - `MetaXRFeature: ErrorFormFactorUnavailable`（沒有頭盔）
  - Vivox `403`
  - KriptoFX 水面的 NullReference
  - XRI 的 `8/8 slots used`（rig 本身就有的問題）
