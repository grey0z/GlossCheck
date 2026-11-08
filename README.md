# GlossCheck – Máy đo độ bóng 2 jig (viết lại từ ColorMeasure)

Phần cứng:
- 2 trục XY Fastech Ezi-SERVO Plus-E
- 2 xi lanh nâng jig dùng van 2 cuộn
- **2 bộ hút chân không** giữ hàng trên 2 jig (van + công tắc chân không)
- Máy đo độ bóng NHG (20/60/85°, qua `NGComm.dll`)
- Nút **START / STOP**; **không có nút EMG và nút RESET** (nút STOP kiêm RESET)
- **Còi (buzzer)**
- Module IO Ezi-IO Plus-E, cần ít nhất 10 input / 13 output
- Không có cảm biến cửa

Toàn bộ code được viết lại theo kiến trúc của EziMotionDemo: một thread quét kiểu PLC, luồng viết bằng `yield return`, interlock nằm ở tầng thiết bị.

## Chạy

```bash
dotnet build GlossCheck.slnx
```

Chạy `GlossCheck\bin\Debug\net48\GlossCheck.exe`. Mặc định là chế độ **mô phỏng**, không cần phần cứng.

- Lần chạy đầu, chương trình tự tạo `MachineConfig.xml` và `Recipes\Default.json` cạnh file exe.
- Kết quả đo ghi vào `Results\yyyy-MM-dd.csv` (mở được bằng Excel).
- Log ghi vào `Logs\yyyy-MM-dd.log`.
- **Bảng bộ đếm sản lượng** ở tab Auto: các dòng Jig 1 / Jig 2 / TỔNG, mỗi dòng có OK, NG, Tổng, Tỉ lệ đạt.
  - Số đếm lưu vào `Counters.xml` sau mỗi lần đo: tắt / mở lại không mất.
  - Chỉ về 0 khi bấm "Xoá bộ đếm"; thời điểm xoá gần nhất hiện ở dòng "Tính từ".

| File | Lưu gì |
|---|---|
| `MachineConfig.xml` | Thông số máy: IP, pulse/mm, giới hạn mềm, tốc độ, timeout, model đang dùng |
| `Recipes\<model>.json` | Theo từng sản phẩm: **vị trí teach** Pos1 (Jig 1 vào, cũng là Ready) / Pos2 (Jig 2 vào), đơn vị mm; spec 3 góc; số lần đo |
| `Counters.xml` | Bộ đếm OK/NG của 2 jig và thời điểm xoá gần nhất |

**Tab Sản lượng** đọc thẳng từ các file CSV trong `Results`, nên số liệu luôn khớp với file:
- Bảng theo giờ của 1 ngày: OK/NG từng jig, tổng, tỉ lệ đạt, và dòng TỔNG NGÀY. Mặc định ẩn các giờ không có hàng.
- Bảng tổng 7 ngày gần nhất. Double-click một ngày để xem chi tiết theo giờ.
- Lọc theo model. Đang xem hôm nay thì bảng tự cập nhật sau mỗi lần đo.

**Thử nhanh:** bấm **ORIGIN**, rồi **START** (Jig 1 được đo). Bấm START lần nữa thì Jig 2 được đo.

Ở chế độ mô phỏng, cửa sổ popup **"Mô phỏng phần cứng"** tự mở khi khởi động:
- Nút cứng START / STOP; ô "Mất kết nối IO board" để thử trường hợp dừng khẩn cấp.
- "Có hàng trên Jig 1 / Jig 2": bỏ tick để thử trường hợp không hút được hàng.
- Giả lập lỗi: kẹt xi lanh, máy đo treo, Jig 2 ra NG.
- Trạng thái XY, xi lanh, hút chân không, còi.

Đóng popup chỉ ẩn nó đi, các lỗi đang giả lập vẫn giữ. Mở lại bằng nút **MÔ PHỎNG** trên header.

## Cách vận hành

**Cơ cấu:**
- **Trục Y** nằm giữa, mang 2 jig (mỗi bên 1 jig). Y chạy thì jig này vào, jig kia ra.
- **Trục X** đưa máy đo độ bóng sang bên có jig đang ở trong.
- **Pos1:** Jig 1 (trái) ở trong, máy đo ở bên trái; Jig 2 (phải) ở ngoài để nạp hàng.
- **Pos2:** ngược lại.
- Mỗi vị trí teach cả X (máy đo) và Y (con thoi). X và Y chạy nội suy cùng lúc, sau khi đã hạ cả 2 xi lanh.
- Vị trí **Ready = Pos1**, không teach riêng.

| Nút | Đang ở trạng thái | Tác dụng |
|---|---|---|
| ORIGIN | Chưa origin / Sẵn sàng | Bật servo, hạ 2 xi lanh, origin X, Y rồi về Ready (Pos1: Jig 1 vào, Jig 2 ra) |
| START | Sẵn sàng | **Đo 1 lần** jig đang ở ngoài (xem quy trình bên dưới), rồi về Sẵn sàng. Sau ORIGIN, jig ở ngoài là Jig 2 |
| START | Đang chạy (đang đo) | Bỏ qua, chờ đo xong |
| START | Tạm dừng | Đo tiếp đúng bước đang dở |
| STOP | Đang chạy | Tạm dừng: trục dừng; xi lanh và hút chân không giữ nguyên |
| STOP (nút cứng) | Alarm / Dừng khẩn cấp | **Kiêm RESET:** xoá alarm. Lỗi trục / mất kết nối IO thì sau đó phải ORIGIN lại; lỗi khác thì máy về Tạm dừng |
| STOP (nút cứng) | Tạm dừng | **Kiêm RESET:** bỏ lần đo đang dở (không ghi kết quả), về Sẵn sàng |
| RESET (trên màn hình) | Alarm / Tạm dừng / Dừng khẩn cấp | Giống STOP cứng ở các trạng thái này. Khi IO board vẫn mất kết nối thì bị từ chối |

**Quy trình 1 lần đo:**
1. Đặt hàng lên jig đang ở ngoài, bấm START.
2. Bật hút chân không jig đó. Công tắc chân không không báo "đã hút" trong `Thời gian chờ hút` (mặc định 1.5 s) thì máy coi là **không có hàng / hàng lệch**: nhả hút, còi kêu 2 tiếng, hiện cảnh báo đỏ, về Sẵn sàng. Trường hợp này **không báo alarm**, đặt lại hàng rồi bấm START.
3. Hạ cả 2 xi lanh, con thoi đưa jig đó vào và máy đo sang bên đó. Jig kia ra ngoài và **nhả hút** để lấy hàng đã đo.
4. Kiểm tra còn giữ hàng, nâng xi lanh, đo, kiểm tra lại, hạ xi lanh. Mất chân không lúc này báo alarm **E310 / E311** vì kết quả không còn tin cậy.
5. Hiện OK/NG và ghi CSV; NG thì còi kêu 3 tiếng ngắn. Jig vừa đo nằm ở trong, **vẫn hút giữ hàng** tới lần START sau (hoặc ĐỔI JIG).

**Còi:**
- Alarm / dừng khẩn cấp: kêu ngắt quãng tới khi RESET.
- NG: 3 tiếng ngắn.
- Không hút được hàng: 2 tiếng dài.
- Tuỳ chọn 1 tiếng ngắn khi OK (tab Cấu hình → Còi).

**Đèn nút STOP:** sáng khi Tạm dừng, nhấp nháy khi alarm (nghĩa là bấm STOP để reset).
**Không có nút EMG:**
- Trạng thái **Dừng khẩn cấp** chỉ còn dùng khi **mất kết nối IO board** (E002). Lúc đó máy không đọc được nút STOP và cảm biến, nên dừng trục, tắt servo, bắt buộc ORIGIN lại.
- Khi có IO lại: bấm "Kết nối lại" ở thanh dưới (máy thật), rồi STOP cứng hoặc RESET, rồi ORIGIN.
- ⚠ Nút STOP là tín hiệu phần mềm, đi qua IO board và vòng quét, **không phải dừng an toàn**. Nên có thiết bị an toàn phía điện (rơ-le an toàn cắt nguồn động lực servo, cầu dao…) theo quy định an toàn của xưởng.

**Nút TẮT CÒI** (header):
- Bấm được khi còi đang kêu; lúc đó nút nhấp nháy cam.
- Bấm thì còi im ngay, nhưng alarm vẫn còn và vẫn phải RESET.
- Có alarm hoặc tiếng bíp mới thì còi kêu lại.

**Thanh kết nối** (đáy màn hình): mỗi thiết bị 1 đèn.
- Thiết bị: **Trục X**, **Trục Y**, **IO board**, **Máy đo gloss** (kèm cổng COM; rê chuột để xem model, số serial, trạng thái calib).
- Màu đèn: **xanh** = đã kết nối, **đỏ** = mất kết nối, **cam** = máy đo chưa calib / đang kết nối, **xám** = mô phỏng.
- Nút **Kết nối lại** thử kết nối lại các thiết bị đang mất kết nối; không dùng được khi máy đang chạy. Kết nối lại xong vẫn phải RESET / ORIGIN như bình thường.

**Trên tab Auto:**
- Mỗi khung jig ghi rõ jig đó đang **Ở TRONG** (dưới máy đo) hay **Ở NGOÀI** (nạp hàng).
- Mỗi khung jig ghi trạng thái hút: tắt / ĐANG GIỮ HÀNG / bật nhưng chưa hút được.
- Nút **ĐỔI JIG**: chạy con thoi sang vị trí còn lại mà **không đo**, dùng để lấy jig đang ở trong ra (vd. sản phẩm cuối ca). Jig đi ra được nhả hút.
  - Có hộp xác nhận "bỏ tay ra khỏi jig" trước khi chạy.
  - Chỉ bấm được khi máy Sẵn sàng.
  - Bấm STOP thì dừng lệnh đổi jig.

## Cấu trúc code

| Thư mục / namespace | Nội dung |
|---|---|
| `Hardware` | Interface `IAxis`, `IIoModule`, `IGlossMeter`, `ILinearInterpolator`; bản đồ IO `InputSignal` / `OutputSignal`; `GlossReading` |
| `Hardware.Fastech` | `EziServoAxis`, `EziLinearInterpolator` (`FAS_MoveLinearAbsPos`), `EziIoModule`, `EziNativeMethods` (P/Invoke) |
| `Hardware.Nhg` | `NhgGlossMeter`, `NhgNativeMethods` |
| `Hardware.Simulation` | `SimWorld`, `SimAxis`, `SimIoModule`, `SimGlossMeter` |
| `Core` | `Machine` (trạng thái, vòng quét, nút cứng, đèn), `Sequence` + các `Step`, `Cylinder`, `Gantry` (interlock), `JigStation`, `GlossMeterService` (máy đo chạy thread riêng), `MachineAlarm` (mã E001…) |
| `Sequences` | `HomingSequence`, `MeasureSequence` |
| `Settings` | `MachineConfig` (XML), `Recipe` / `TeachPoints` / `AngleSpec` (JSON), `RecipeStore` |
| `Data` | `JudgeResult`, `ResultLogger` (CSV), `TextLog` |
| `UI` | `MainForm` cùng 5 tab: `AutoPage` (`JigPanel`), `ManualPage`, `TeachingPage` (`JogPanel`), `RecipePage`, `ConfigPage` |

**Luồng đo** (`Sequences/MeasureSequence.cs`, rút gọn):

```csharp
yield return Do("Bật hút chân không " + jig.Name, () => jig.Vacuum.Command(true));
var hold = new TryWaitStep("Chờ hút được hàng", () => jig.Vacuum.IsHolding, vacuum.HoldTimeoutMs);
yield return hold;
if (hold.TimedOut) { jig.Vacuum.Command(false); m.ReportNoPart(jig); yield break; } // không có hàng: huỷ, không alarm
yield return Cylinder.MoveAll("Hạ cả 2 xi lanh jig", false, timeouts.CylinderMs, m.Jig1.Lifter, m.Jig2.Lifter);
yield return m.Gantry.MoveTo("Đưa " + jig.Name + " vào vị trí đo", recipe.Points.ForJig(jig.Id));
yield return Do("Nhả chân không " + other.Name, () => other.Vacuum.Command(false));
yield return jig.Lifter.MoveUp(timeouts.CylinderMs);
yield return measure;   // MeasureStep: máy đo chạy thread riêng
yield return jig.Lifter.MoveDown(timeouts.CylinderMs);
yield return Do("Phán định và ghi kết quả", () => m.RecordResult(jig, recipe, measure.Reading));
```

## Bản đồ IO

| Input | | Output | |
|---|---|---|---|
| IN0 | Nút START | OUT0 / 1 / 2 | Đèn tháp xanh / vàng / đỏ |
| IN1 | Nút STOP (kiêm RESET) | OUT3 | Đèn nút START |
| IN2 | *(trống, trước là RESET)* | OUT4 | *(trống, trước là đèn RESET)* |
| IN3 | *(trống, trước là EMG)* | OUT5 | Đèn nút STOP |
| IN4 / IN5 | Xi lanh Jig 1 xuống / lên | OUT6 | **Còi (buzzer)** |
| IN6 / IN7 | Xi lanh Jig 2 xuống / lên | OUT7 / OUT8 | Van Jig 1 lên / xuống |
| **IN8** | **Công tắc chân không Jig 1** (ON = đã hút) | OUT9 / OUT10 | Van Jig 2 lên / xuống |
| **IN9** | **Công tắc chân không Jig 2** | **OUT11 / OUT12** | **Van hút chân không Jig 1 / Jig 2** |

Các bit của **vacuum (IN8, IN9, OUT11, OUT12)** là mặc định tôi đặt, **cần đối chiếu với bản vẽ điện**. Muốn đổi IO thì sửa giá trị trong `Hardware/IoSignals.cs`.

Nếu chưa lắp công tắc chân không: tab Cấu hình → Hút chân không → `Dùng công tắc chân không = false`. Khi đó máy chỉ bật van rồi chờ, không phát hiện được thiếu hàng hay mất hút.

## Những điểm đã sửa so với ColorMeasure

- **Timeout:** mọi bước chờ đều có timeout và mã alarm riêng. Code cũ không bao giờ báo timeout xi lanh ở bước lên/xuống, và không báo khi trục dừng sai vị trí.
- **Tốc độ nội suy:** tính thẳng từ mm/s, không còn lỗi tràn số `uint` như code cũ.
- **Máy đo không chặn vòng quét:** máy đo chạy trên thread riêng. Nếu DLL treo, máy báo E401 và vòng quét vẫn chạy bình thường.
- **Xác định jig đang đo:** dựa trên trạng thái jig, không so encoder bằng `==`.
- **Phán định:** tính cả biên (≥ Min, ≤ Max). Nhãn góc đúng là 20/60/85°. Có tuỳ chọn đo N lần lấy trung bình. Mọi lần đo đều ghi CSV.
- **IO xi lanh 2:** code cũ cấu hình sai, giờ khai báo đúng.
- **Chạy tay:** "Đi tới" bằng tay và jog đều bị interlock (xi lanh phải ở dưới).
- **Mất kết nối IO:** máy chuyển sang Dừng khẩn cấp (E002); không RESET được khi IO chưa kết nối lại.

## Checklist khi lên máy thật

1. Mở tab **Cấu hình** và chỉnh:
   - Chế độ chạy = `Real`.
   - IP của trục X/Y. Mặc định là .20 / .21; BoardId = số cuối của IP.
   - IP module input/output: cùng 1 module thì nhập cùng IP.
   - `Pulse / mm` (mặc định 1000) và giới hạn mềm.
   
   Lưu rồi **khởi động lại phần mềm**.
2. Kiểm tra `EziMOTIONPlusE.dll` và `NGComm.dll` (bản x64) đã nằm cạnh exe. Bước build tự chép từ `Lib\x64`.
3. Mở tab **Thao tác tay → IO**: bấm từng nút cứng và đẩy tay từng xi lanh, kiểm tra input đổi đúng bit. Input nào đi dây NC (logic ngược) thì bật bit tương ứng trong `Mặt nạ đảo input`.
4. Kiểm tra từng van (double-click output OUT7…OUT10): xi lanh phải lên/xuống đúng jig. Code cũ cấu hình xi lanh 2 sai, nên **bắt buộc kiểm tra bước này**.
   - Kiểm tra hút chân không bằng các nút **Jig 1/2 HÚT / NHẢ** ở tab Thao tác tay: đặt hàng lên thì nút HÚT phải sáng **xanh** (IN8/IN9 ON); bỏ hàng ra thì sáng **vàng** (chưa hút được). Chỉnh `Thời gian chờ hút` nếu bơm/ejector hút chậm.
   - Bấm thử STOP cứng khi đang alarm: phải xoá được alarm (nút STOP kiêm RESET).
5. Kiểm tra máy đo: bấm "Kết nối lại", rồi "Đo thử". Nếu báo "CHƯA CALIB" thì bấm Calib.
6. Kiểu origin (limit / cảm biến ORG / Z-pulse) cài trong tham số driver bằng phần mềm GUI của Fastech. Chạy ORIGIN ở tốc độ thấp lần đầu.
7. Tab **Teaching**: jog tới khi Jig 1 nằm đúng dưới đầu đo, bấm "Lấy vị trí hiện tại" cho **Pos1**. Làm tương tự với Jig 2 cho **Pos2**. Bấm "Đi tới" để kiểm tra, rồi "Lưu vào model". Recipe cũ của ColorMeasure lưu theo pulse nên phải teach lại (đơn vị mới là mm).
8. Chạy auto với hàng mẫu đã biết kết quả, đối chiếu với file CSV.
