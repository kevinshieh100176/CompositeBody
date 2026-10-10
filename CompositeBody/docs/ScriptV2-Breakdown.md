# 《合成肉身》Script V.2 — breakdown and work plan

Sources, both read 2026-10-08:

- `《合成肉身》Prototype 開發清單／腳本／文本資料 V.2.pdf` — the script (11月 v2).
- `合成肉身_VR_Production_Timeline_v2.xlsm` — the **production cue sheet**, 3 tabs
  (`VR Timeline`, `跨組總覽`, `欄位與規則`), last edited 2026-10-07 by 薛祖杰.
  Where the two disagree, **the timeline wins** — it is the cross-department table the art,
  engineering and sound teams are working from.

**Decisions taken 2026-10-08:** 真人 A/B are **scanned point clouds** (already written into the
timeline) · the two players are **in the same room** · **no scope cut** — build in script order
and accept that November is partial · numbering follows the script's detail body, extended in §2.

> **The timeline is a runtime cue sheet, not a schedule.** It has no dates, no owners, no
> milestones, and no shoot or recording bookings. So the questions in §8 are still open — this
> document cannot answer when anything is due or who is doing it.

---

## 1. What the piece is now

**38 minutes exactly** (2280 s), two players, co-located. The timeline is authoritative and runs
**9 min 20 s longer than the PDF's own duration markings** — it lengthens almost every beat,
and shortens only S2-4.

| Act | Title | Runtime | Share |
|---|---|---|---|
| O | Onboarding：學會成為鬼 | 5:10 | 14 % |
| S1 | 轉生：從共同生活中形成 | 2:40 | 7 % |
| S2 | 鬼的任務：把不完整的記憶重新拼回來 | 9:00 | 24 % |
| S3A | 把家道別：發現自己是留在對方身上的形狀 | **11:10** | 29 % |
| S3B | 離開：替他們完成最後一次傳遞 | 10:00 | 26 % |
| | **total** | **38:00** | |

**This reweights the whole plan.** 把家道別 is now the longest act, not 鬼的任務, and the back
half (S3A + S3B) is **21:10 — 56 % of the runtime**. The three longest beats in the piece are
S3A-2 (5:00), S3A-4 (4:00) and S3B-2 (4:00), and **all three are 真人 beats**. Over half the show
sits downstream of a capture shoot that has not happened.

The structural idea is unchanged: *the two players are not the lovers, they are the membrane left
behind on each other*, and **the same event exists in two different memories**, one per headset.
The timeline states the design rule for it directly — 「每個互動都要有『另一個人知道我不知道的
事』」.

---

## 2. Numbering — reconciled

Three conventions are in play. The script's outline, the script's detail body, and the timeline:

| Act | PDF outline | PDF body | Timeline | **Use in code** |
|---|---|---|---|---|
| Onboarding | O | O | O | `O*` |
| 轉生 | S0 | S1 | S1 | `S1_*` |
| 鬼的任務 | S1 | S2 | S2 | `S2_*` |
| 把家道別 | S2 | S3 | **S3A** | `S3_*` |
| 離開 | S3 | S4 | **S3B** | `S4_*` |

The front half is settled — body and timeline agree, and that was the decision taken.

The back half needs a call. The timeline invented **S3A / S3B** because the draft it was built
from numbered *both* back-half acts "S3" — its own note says so, and calls the split provisional:
「此表為製作方便暫分S3A（把家道別）與S3B（離開），未改動故事內容」. **The V.2 PDF has since
fixed that properly by numbering 離開 as S4.** So code should use **S3 / S4**, which is the newer
and cleaner convention, *and* carry the timeline's id in the beat's display name so the staff
panel reads `S4-2 Relay the Words (timeline S3B-2)` and nobody has to translate in their head.

Also note, when reading the PDF: its S4-1 and S4-2 both cite "S2-4" for content that is in S3-4.
The timeline hit the same thing and labelled two rows 「原稿標記S2-3」/「原稿標記S2-4」.

**Naming rule:** enum members carry number *and* content — `S4_2_RelayTheWords`, never `S4_2`.
The number cross-references the documents; the content suffix makes the next renumber a
mechanical rename instead of guesswork.

> **Existing bug this exposes:** `StoryBeats.ShortCode` maps `S0_1…S0_4` to `"S1".."S4"`. Those
> now collide head-on with the real S1–S4 acts on the staff panel. Must become `"S1-1"`, `"S1-2"`, …

---

## 3. What is already built and carries straight over

The spine is in good shape. This is the part that does *not* need rethinking.

**Beat spine** — `ExperienceDirector` (server-authoritative linear chain; three exit conditions:
both-player gate, authored duration, staff override), `StoryBeat` + `StoryBeats`,
`BeatController`, `PlaceholderBeat`, `StaffControlPanel`, `StoryProgressManager` (the
both-players-must-report primitive). V.2 needs **new enum entries and new content, not a new
architecture.** The staff override in particular is now a stated production requirement, not a
debug nicety — see §5.

**Per-role divergence** — `RoleVisibility`, `RoleGhost`, `RoleTint`, `PlayerRoleColors`,
`LocalRolePresenter`, `RoleSpawnPositioner`. Exactly the 不對稱資訊 structure the timeline asks
for in every act.

**Two-player assembly** — `CompositeHalf` already implements S2-2 and S2-3 almost exactly: paired
halves, one owner each, owner sees solid while the other sees ghosted, and the server welds only
when **both players hold their own half and bring them together**. Plus `MeshHalfSplitter` +
`ChairHalves` to author splits with capped cut faces. The timeline's ask — 「Asymmetric
visibility；Snap/Assembly；雙人共同物件同步」— is this component.

**Membrane look** — `VacuumMembrane` / `VacuumMembraneAnimated`, `MembraneReveal` (fade or
grow-from-a-point via `_Reveal`), `MembraneShellBuilder`, `MembraneWrapBuilder`,
`MembraneFilmLink`, `MembraneCocoonGenerator`, `DrapedFigureMesh`, `TensionSkinBuilder`,
`GooShellBuilder`. The player avatar is already the Ch36 membrane figure.

**O-0 as a worked vertical slice** — `O0ArrivalBeat`, `RoleBlobPresenter`, `DistantSoundBed`,
`ScreenFade`, `FrameDrift`, `ExperienceClock`, with placeholder audio. The template for every
other beat.

**Cord** — `PlayerTether` + `TetherCord`. Reusable for the linked handsets.

**Session / build** — LAN discovery + `GameSessionManager`, `DesktopXrFallback`,
`BuildPcvrPlayer`, `SetBuildScenes`, batch-mode harnesses, `Build*Scene.cs` builders.

**Vivox** — `com.unity.services.vivox` and `VoiceChatManager` are in the project. Co-location
means no voice *transport* is needed, but see M10: the timeline does want the world to react to
speech, so this is not simply dead weight.

---

## 4. The breakdown: ten reusable mechanics

V.2 reads as five acts but is really ten recurring verbs. Act-by-act you would build each one
three or four times. Ordered by how many beats each unlocks.

### M1 · Role-owned grabbable, with colour-seep and grab-triggered VO
*Needed by: O-1, S3-2, S4-2.*
Colour 「從物體內部慢慢浮出」for the owner only, explicitly **not** a game-style hard outline;
the other player's hand passes through. On grab it plays a recorded fragment. Ownership and
ghosting already live inside `CompositeHalf` — **extract them into a standalone component**.

The timeline **changed the prop list** from the PDF: **A** gets 男用外套 + 杯子, **B** gets
鑰匙 + 狗布偶 — the cup moved from B to A, and B's two are new, with new lines
(「我覺得這裡是我夢想的家，太棒了，謝謝寶寶。」) and dog sounds. S3-2 adds 「物件不掉落／防遺失
機制」: a held memory object must not be droppable mid-monologue.

### M2 · Light zone (光區) with occupancy and colour mixing
*Needed by: O-2, S1-2, S3-1, S4-3.*
A coloured floor zone that knows who is in it, mixes toward a new colour as the players
converge, and drives audio clarity and particle density off that mix. Four beats, four
dressings, one component.

### M3 · Mutual orbit / music-box rotation
*Needed by: O-0, O-2, S1-2, S3-4, S4-3.*
The signature gesture, in two flavours: **players orbiting each other** (O-2, S4-3) and **the
floor rotating under one player like a music box** (O-0, S1-2). Both should resolve to one 0→1
progress value that audio, VFX and the gate all read. The timeline asks for 「旋轉／角度進度」and
guide arrows. S3-4 also rotates the player's *own* body while they listen.

### M4 · Per-role dialogue cue system
*Needed by: every beat.* The structural core, and the timeline is emphatic about it.
Three modes: **same line to both**, **different line to each** (S2-3: A hears an apology, B hears
an accusation), and **both versions overlapping** then vanishing together.

> 跨組總覽 on S2-4: 「S2-4是最大整合場；文本Cue、破碎動畫、音效必須共用同一Cue表」— the text,
> the destruction animation and the sound effects must run off **one shared cue table**. That is
> a direct instruction to build M4 as the cue spine rather than per-beat timelines, and it is why
> M4 comes early.

Now that the players share a room they will hear each other and simply compare out loud. That is
probably the point, but the divergent-memory beats should be designed knowing it happens
immediately.

### M5 · Sustained touch on a figure
*Needed by: O-0, S3-4.*
O-0: touching the 真人 births your hands. S3-4: the voice pressed inside the membrane surfaces
only while contact is **maintained** — 「玩家必須維持觸碰，才能聽見完整的話」. The timeline
specifies 「Audio reveal by hold duration」. A contact-duration interactor, not a trigger. Needs
M6's proxy colliders.

### M6 · 真人 as scanned point cloud
*Needed by: O-0, S1-1, S3-1→S3-4, S4-1.* **The largest new system, the longest lead, and now
~56 % of the runtime depends on it.**

Choosing volumetric scan is mostly **good news**, because it unifies three systems:

- The dissolve-to-particle exits become nearly free — the figures already *are* particles, so
  **M7 collapses into the same renderer**.
- S1-1's 點雲 world shares the playback path.
- The S3-1 glitch becomes a point-size / jitter / colour pass, not a new skinned shader.

Costs:

- **The capture shoot gates all of S3 and S4** — 21 of the 38 minutes. This is the critical path,
  full stop.
- **Playback pipeline** — format (PLY/Draco sequence, Alembic, VFX Graph point cache), per-frame
  streaming across 38 minutes on PCVR, **two figures on screen at once** in S3, and the
  timeline's 「遠景LOD」for S1-1's distance work.
- **Static poses vs animated sequences.** S3-1 reads as a *held* emotional pose
  (「保持靜止，像停留在記憶中的某一刻，但是非常情緒化的動作」), which points at static scans with
  crossfades — far cheaper than motion. **Prototype this before booking a shoot that assumes
  motion.**
- **Proxy collider rig inside the cloud.** A point cloud has nothing for M5's touch or the hug to
  hit.
- **Role-swapped assignment** — Player A faces 真人 B. The timeline flags this as unlocked:
  「顏色／真人依附規則需定稿」.

### M7 · Particle dissolve travelling onto a body
*Needed by: S3-3, S4-1, S4-3.*
Stone and T-shirt break into purple/yellow particles that run **along the arm and chest** and
reach across to the 真人. The moment the player understands what they are. Folds into M6's
renderer; `PowderBurst.vfx` and `TraceParticle.shader` are the start of the travel path.

### M8 · Progressive destruction of the house
*Needed by: S2-4 — one beat, 3:00, and the act's climax.*
Every line of a ~15-exchange argument shatters more of the home 「像是一座沙堡突然被擊碎」,
ending with only a patch of floor underfoot. Bespoke, expensive, and bound to M4's cue table.
The timeline also wants a 外派手機畫面 asset and 「破碎狀態同步」across both clients.

### M9 · Grab-to-ascend locomotion
*Needed by: S1-2.*
「抓握上升」into the hanging/inverted house. **The timeline gates this:
「上升／移動方式需先做Comfort Prototype」** — a comfort test comes before the real
implementation, which is right for the one novel locomotion verb in a 38-minute piece.

### M10 · Voice activity, not speech recognition
*Needed by: S4-2.* **I had this wrong when I only had the script.**
Co-location means the players can just talk — but the timeline wants the *world* to respond:
「語音輸入偵測（只需voice activity或可錄音）」, and 跨組總覽 is explicit —
「不要要求語意辨識，只需要讓『說出來』被世界感知」. So: voice-activity detection driving
spatial echo and particle response, background ducking while they speak, and a per-player
"has spoken" flag feeding the gate. No ASR, no transcription. Cheap, and it is the climax.

### Smaller one-offs
- **Dust sweep to reveal** (S2-3): 「手勢揮掃判定」clears dust so the cup fragments spawn.
- **Membrane-wrapped incomplete furniture** (S2-2): a room of props in three states — half
  missing, floating, outline only. An authoring pass; `MembraneRoom` / `MembranePropChair` are
  the start.
- **Hug detection** (S3-4), with a fallback — see §5.
- **Two doors** (S4-4), which must line up with the physical play area.
- **CJK text in VR** (O-0's title card and opening line). **No CJK TMP font asset exists** — only
  Liberation Sans and Inter. Cheap to fix now, annoying later.

---

## 5. Cross-cutting rules the timeline imposes

These are not mechanics; they are constraints on all of them, and they are stated as rules.

**Nobody may be stuck waiting for the other player.**
「Onboarding三項條件都要有容錯，不可讓一位玩家卡住另一位」. Every gated beat needs a timeout
fallback as well as its gate — the timeline names 「真人消散fallback」and
「兩邊語音皆完成或timeout」. The existing staff override covers the venue case; this asks for
automatic recovery too. **Design each gate with its timeout from the start**, rather than adding
them after the first time a show hangs.

**Refusing an intimate action must still complete the beat.**
「要讓拒絕擁抱也能完成」. A player who will not hug, or cannot bring themselves to speak in
S4-2, must still reach the end. Build the opt-out path at the same time as the action.

**The physical room is part of the design.** The doors must align with the real play area
(「雙門位置與Physical play area對齊」, 「安全邊界提醒」), and the orbit beats put two people
circling each other in the dark. With co-location confirmed, the shared origin has to be right —
the pinch calibrator was stood down in favour of the headset's own room setup, so that needs a
verification pass before S3 work starts.

**Onboarding must not feel like a tutorial.**
「把操作教學包進世界觀；玩家不應覺得是在做Tutorial」.

---

## 6. Beat table → code

Durations are the timeline's. Enum values keep the existing spacing-by-10, and renames hold their
value — Unity serializes enums by int, so a rename at a fixed value does not touch authored scenes.

| Timeline | PDF | Beat | Dur | Enum member | Value | Exit | Mechanics |
|---|---|---|---|---|---|---|---|
| O-0 | O-0 | 進入，揮手開啟旅程 | 1:00 | `O0_Arrival` | 10 *(keep)* | gate | M3 M5 M6 |
| — | — | *(V.1 意念干擾物件 — cut)* | — | `O1_Control` | 20 *(retire)* | — | — |
| O-1 | O-1 | 鬼的抓取 | 1:30 | `O1_Grasp` | 30 *(rename)* | gate | M1 |
| — | — | **停頓點** — see note | 0:00 | *(not a beat)* | — | — | — |
| O-2 | O-2 | 鬼魂交流 | 2:00 | `O2_Communion` | 40 *(rename)* | gate | M2 M3 M4 |
| O-3 | O-3 | Onboarding 結束 | 0:40 | `O3_OnboardingEnd` | 50 *(rename)* | timed | — |
| S1-1 | S1-1 | 記憶痕跡開始形成空間 | 0:40 | `S1_1_TracesFormSpace` | 60 *(rename)* | timed | M6 |
| S1-2 | S1-2 | 吊掛的家出現 | 2:00 | `S1_2_HangingHouse` | 70 *(rename)* | gate | M3 M4 **M9** |
| S2-1 | S2-1 | 門關上之後 | 1:00 | `S2_1_AfterTheDoor` | 110 | timed | — |
| S2-2 | S2-2 | 第一個碎片：燈 | 3:00 | `S2_2_LampFragment` | 120 | gate | **CompositeHalf** M4 |
| S2-3 | S2-3 | 第二個碎片：杯子 | 2:00 | `S2_3_CupFragment` | 130 | gate | **CompositeHalf** M4 dust |
| S2-4 | S2-4 | 隱藏的記憶／家崩毀 | 3:00 | `S2_4_HiddenMemory` | 140 | timed | M4 **M8** |
| S3A-1 | S3-1 | 兩個燈區，真人出現 | 0:40 | `S3_1_TwoLightZones` | 150 | timed | M2 M6 |
| S3A-2 | S3-2 | 回到自己所依附的人 | **5:00** | `S3_2_TheirObject` | 160 | gate | M1 M4 M6 |
| S3A-3 | S3-3 | 物件化膜 | 1:30 | `S3_3_ObjectDissolves` | 170 | timed | **M7** M6 |
| S3A-4 | S3-4 | 沒有說出口的話 | **4:00** | `S3_4_UnspokenWords` | 180 | gate | **M5** M3 M4 M6 hug |
| S3B-1 | S4-1 | 真人消失 | 1:00 | `S4_1_TheyDissolve` | 190 | timed | M7 M6 |
| S3B-2 | S4-2 | 場景成為記憶／傳話 | **4:00** | `S4_2_RelayTheWords` | 200 | gate | **M10** M1 M4 |
| S3B-3 | S4-3 | 最後一次旋轉 | 3:00 | `S4_3_LastOrbit` | 210 | timed | M2 M3 M7 |
| S3B-4 | S4-4 | 膜鬆開 | 2:00 | `S4_4_MembraneLoosens` | 220 | timed | doors, fade |
| — | — | terminal hold | — | `End` | 100 *(keep)* | staff | — |

**停頓點.** The timeline has a zero-length row at TC 2:30, right after O-1, reading
「停頓點（互動需要做完兩個）」. Read against 跨組總覽's 「Onboarding三項條件」, this is most
likely *two of the three onboarding conditions must be done before continuing* — but it could
also mean *each player must grab both of their own objects*. It is a gate condition, so it needs
settling before O-1 is built. Flagged in §8.

**Retired from V.1:** `O1_Control` (20 — the fist/anti-gravity beat is cut),
`S0_3_EmptyRoomForms` (80) and `S0_4_TheyLeave` (90). The couple's departure moves offscreen and
survives only as the door-shut sound opening S2-1.

`End` keeps value 100 even though it now sits mid-range. `StoryBeats.Ordered` is the running
order and nothing sorts by enum value, so this is safe and cheaper than touching authored scenes.

---

## 7. Plan of work

No scope cut, so two things matter more than they otherwise would: the **placeholder spine**, since
it is what carries the unbuilt 56 %, and **ordering**, so that what slips is chosen rather than
accidental.

### Phase 0 — Lock the spine (1–2 days)
1. Rename and extend `StoryBeat` / `Ordered` / `DisplayName` / `ShortCode` per §6, with the
   timeline id in the display name. Fix the `ShortCode` collision; retire the three dead beats.
2. Put the timeline's durations into `ExperienceDirector.DefaultBeats()` — 2280 s total.
3. Rebuild the placeholder scene so **all 19 beats are walkable end to end**, synopses in place.
4. Add a CJK TMP font asset.

**Done when:** two machines walk the full 38 minutes against markers and staff can jump to any
beat. That is the timing truth to rehearse against, and under a no-cut plan it is also what keeps
the unfinished half presentable.

### Phase 1 — The verb layer
Each as a standalone system with its own harness scene and `Build*Scene.cs` builder — the pattern
the project already uses.

1. **M6 point-cloud spike — first, and out of order.** Not the system: just enough to answer
   *static poses or motion sequences?* and *what does one figure cost per frame?* The shoot
   depends on both answers and 56 % of the runtime depends on the shoot.
2. **M9 comfort prototype** — also out of order, because the timeline requires it before S1-2 and
   a comfort failure here is a design change, not a bug fix.
3. **M4** per-role cue system — the shared cue table S2-4 is specified to need
4. **M1** role-owned grabbable — mostly an extraction from `CompositeHalf`
5. **M2** light zone
6. **M3** orbit / music-box rotation
7. **M10** voice activity — cheap, and it is the climax
8. **M5** sustained touch + M6's proxy collider rig
9. **M7** dissolve travel path — folds into M6's renderer
10. **M8** house destruction — most expensive; last

### Phase 2 — Vertical slices, in script order
O-0 → O-1 → O-2 → O-3 → S1-1 → … → S4-4. One beat at a time, swapping a placeholder for real
content without the spine changing, as O-0 was done. A slice is done when it runs on two
headsets, in the room, with its real audio — and with its timeout and opt-out paths, per §5.

### Phase 3 — Content authoring (**start now, in parallel**)
The critical path, and none of it is code:

- **The capture shoot.** Gates 21 of the 38 minutes. Blocked only on the Phase 1 spike, which is
  why that spike is first.
- **~38 minutes of VO**, two performers. All written: two S1-2 monologues, the breakup argument,
  two S3-4 monologues, the memory fragments, the final requests. Plus the new O-1 lines and dog
  sounds the timeline added.
- **The house** — furnished, then destructible, in three membrane states.
- **外派手機畫面**, the stone, the T-shirt, 鳥模型, 盆栽, the two doors.
- Music: 「需要大量音樂、奇幻、詭異、繽紛」.

### What should slip, if something must
Least painful first:

1. **S2-4's line-by-line destruction** (M8) — hold the beat with a simpler collapse; the dialogue
   still lands.
2. **S1-2's ascent** (M9) — fade up into the house instead of 抓握上升, if the comfort prototype
   is unconvincing.
3. **S1-1's point-cloud world** — gets cheap *after* the shoot, and is the easiest 40 seconds to
   stage simply.
4. **S4-3's full orbit** and **S4-4's loosening** — these can run short.

Must **not** slip, because the piece stops meaning anything: S2-3 (two versions of the cup),
S3-3 (the dissolve onto your own body), S3-4 (the unspoken words), S4-2 (the relay).

---

## 8. Still open

The timeline answered a great deal but it is a cue sheet, so none of the scheduling questions:

- **The November date** — and whether it is a showing, a work-in-progress, or a funder
  deliverable. That changes what "accept it slips" costs.
- **Is the capture shoot booked?** It gates 56 % of the runtime. The M6 spike should land first.
- **Is VO recording booked?** ~38 minutes, two performers, script already written.
- **Who is on this?** Ten mechanics can be assigned rather than merely ordered.

Carried from the timeline's own risk column:

- **「顏色／真人依附規則需定稿」** — the colour / 真人 attachment rule. Player A faces 真人 B, and
  this is easy to get backwards in both code and art.
- **停頓點's gate condition** (§6) — two of three onboarding conditions, or both objects each?
- **Static scan vs animated sequences** — answered by the spike, needed before the shoot.
- **Point-cloud format and streaming budget** on PCVR, two figures at once.
- **Shared-origin accuracy** now that the players are co-located.
- **Venue floor plan** — the doors and the orbit beats need real-space bounds and two spawn points
  against the actual room.
