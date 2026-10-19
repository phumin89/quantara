# Hangfire และ Adaptive Exit

แผนสำหรับ implementation และ maintenance เอกสารนี้ไม่เปลี่ยน runtime หรือสิทธิ์ execution

## 1. ผลลัพธ์ที่ต้องการและขอบเขต

1. ข้อความซื้อ/ขายเข้า Telegram มีคิวถาวร ตรวจได้ว่ารอส่ง ส่งสำเร็จ หรือยังยืนยันผลไม่ได้ และไม่ถูกส่งซ้ำเพราะ restart หรืองานซ้อน
2. ระบบคำนวณแผนขายตามเหรียญ กรอบเวลา ความผันผวน แนวโน้ม และต้นทุน โดยเจ้าของไม่ต้องกรอกตัวเลขกลยุทธ์ทุกช่อง
3. การครบเวลาเป็นจุดทบทวนที่มีเงื่อนไขถือต่อ ไม่ใช่คำสั่งขายทุกกรณี แต่ยังมีจุดตัดขาดทุนและเวลาถือสูงสุดที่ตรวจสอบได้
4. วัดผลของ scheduler และทางออกแยกกัน พร้อม rollback ที่รักษาคิว ประวัติ position และขอบเขตเงินจริง

ใช้ objective ปัจจุบันที่เซิร์ฟเวอร์ส่งให้สำหรับบัญชีนั้น โดยอ่าน `targetThb` และ `deadlineExclusive` จาก `objective`/`currentGoal` ตาม policy ที่กำหนดอยู่ วัด equity รวมเงินเติมทุนที่ยืนยันแล้ว และช่องว่าง `max(0, targetThb - equity)` แยกผลตอบแทนจากเงินเติม ไม่ใช้เป้าบังคับกำไรหรือบังคับจำนวนดีล คงฟิลด์เป้า legacy และ rules hash เดิมเพื่อความเข้ากันได้ เอกสารนี้ไม่ได้เพิ่มความสามารถแก้เป้าผ่าน UI

| ขอบเขต     | รายละเอียด                                                                                                           |
| ---------- | -------------------------------------------------------------------------------------------------------------------- |
| รุ่นแรก    | ย้าย Telegram delivery และการเตรียมรายงานใน Portfolio เท่านั้น                                                       |
| งานตามหลัง | งานอื่นมีแผนตามหลัง                                                                                                  |
| ไม่รวม     | Webull, derivatives, การเพิ่มไม้, การขายบางส่วนเพื่อทำกำไร, native OCO หรือ AI เปลี่ยนคำสั่งเงินจริงด้วยภาษาธรรมชาติ |

เอกสารนี้ลงรายละเอียดงาน execution ของ P3/P4 ใน [แผนข่าวและการบริหารแผนเทรด](news-aware-trading-plan.md) เฉพาะ policy แบบ deterministic งานข่าวและ P5 ที่เชื่อมแผน AI กับเงินจริงยังเป็นขอบเขตแยก ไม่บังคับรอข่าวครบก่อนทดสอบทางออก และไม่ให้ข่าวที่ขาดมาลบการป้องกัน position

## 2. Baseline และพฤติกรรมที่ต้องตรวจ

| ส่วน                 | สิ่งที่มีอยู่                                                                                            | ปัญหาที่ต้องแก้หรือรักษาไว้                                                                          |
| -------------------- | -------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| Telegram fill alerts | `TradingWorker.Deliver` เรียก `TradingReportDelivery.Tick` ทุก 30 วินาที ส่งได้ไม่เกิน 4 alerts ต่อ tick | คิวกับการเตรียมรายงานใช้วงจรเดียวกัน ต้องแยกสถานะและติดตามงานค้าง                                    |
| Delivery state       | `TradingRuntime` เป็น canonical state; `TradingAlerts` เป็นข้อมูล mirror ในฐาน Portfolio                 | ใช้ claim/CAS ก่อน HTTP; สถานะ `sending`/`unknown` ไม่อนุญาตให้ส่งใหม่เอง                            |
| Daily reports        | เตรียมรายงาน 23:59 Bangkok พร้อม catch-up/correction; adaptive เก็บรายงานในแอป                           | การเปลี่ยน scheduler ต้องคงวันที่ mode และ revision เดิม ไม่ส่งสรุปหรือคำขอ Approve เพิ่มใน Telegram |
| Adaptive v2          | native 5/15/60 นาที; stop จากโครงสร้าง; target/trailing มี ATR ร่วมกับค่าขั้นต่ำ                         | target และระยะ trailing ถูกกำหนดตอน fill; ยังไม่มีวงจรทบทวนแผนตามตลาดอย่างครบถ้วน                    |
| Time exit            | ถ้ามี structural stop แล้ว ครบ `TimeExitMinutes` จะเลือก Sell โดยไม่ตรวจแนวโน้มใหม่                      | ต้องทดสอบ time exit ตามค่าของ policy และแยกการทำงานของ timer จากผลตอบแทนหลังต้นทุน                   |
| AI Team              | วิจัย/สร้าง assessment มี Stop/Resume, cooldown, งบ และกำหนดเวลาตรวจใหม่                                 | AI ยังไม่เป็นผู้เลือกหรืออนุมัติคำสั่ง AutoLive                                                      |

การยืนยันว่า timer ทำงานไม่พิสูจน์ว่าถือต่อแล้วจะได้กำไรมากกว่า ต้องวัดจากหลักฐานหลังต้นทุนก่อนเปลี่ยน policy

จุดอ้างอิงหลัก: `TradingReportDelivery`, `SqlTradingStore`, `AdaptiveTradingStrategy`, `TradingEngine`, `TradingOverviewProjection`, `CompanyLoop` และ `TradingPanel` ในบริการเดิม ไม่สร้างฐานข้อมูลหรือ engine คู่ขนานที่แย่งเป็นเจ้าของบัญชี

## 3. สถาปัตยกรรมและขอบเขตสิทธิ์

- Portfolio เป็นเจ้าของ outbox, delivery receipt, rules, mandate, position และ order เหมือนเดิม Hangfire เป็นตัวเรียกงาน ไม่ใช่ ledger หรือแหล่งยืนยันผล Telegram/broker
- เพิ่ม `Hangfire.AspNetCore`/`Hangfire.SqlServer` รุ่น stable ที่รองรับ runtime เดิม พร้อม lockfile และตรวจ compatibility ใน H0 เริ่มใช้ใน process Portfolio เดิมด้วย worker จำกัดจำนวน ไม่เพิ่ม service กลางหรือ container ที่อ่านฐานของทุกบริการ
- Hangfire ใช้ schema แยกในฐาน Portfolio บน SQL Server เดิม ใช้สิทธิ์เท่าที่จำเป็นและ migration แบบ additive ถ้าภายหลัง Team/MarketData ใช้ Hangfire ให้มี storage/credentials ของบริการตนเอง
- Jobs รับเพียง account reference, entity key และ contract version ข้อมูลจริงอ่านจาก storage เจ้าของ ณ เวลารัน ไม่เก็บ bot token, broker credentials, prompt หรือข้อความทั้งชุดใน job arguments
- HTTP ระหว่างบริการยังเป็นขอบเขตเดิม Domain ไม่อ้าง Hangfire; adapters อยู่ Infrastructure และเรียก Application ผ่าน DI/CQRS เดิม
- แยก queue สำหรับ Telegram กับ maintenance และจำกัด concurrency ของแต่ละกลุ่ม เพื่อไม่ให้งานรายงาน/วิจัยบังการส่งข้อความหรือแย่งทรัพยากรจาก realtime execution
- SQL transaction/CAS เป็นตัวป้องกัน side effect ซ้ำ ไม่อาศัย `WorkerCount=1`, process semaphore หรือ `DisableConcurrentExecution` เป็นหลักฐานว่าไม่มีงานซ้อน
- Streaming ราคา, SSE, protective monitoring, broker reconciliation, deadline maintenance และ Telegram callback long polling อยู่ใน workers เดิม ไม่มี job ไหนเรียกคำสั่ง Approve/Submit แทนมนุษย์หรือข้าม mandate
- AutoLive v1/v2, ManualReview และ Paper คงความหมายเดิม การเขียนแผน พัฒนา ทดสอบ ติดตั้ง และ restart ไม่เปิด AutoLive

| จุดแก้หลัก   | ขอบเขต                                                                         |
| ------------ | ------------------------------------------------------------------------------ |
| Portfolio    | delivery/report scheduling, Hangfire adapters และ exit plan ในบริการเดิม       |
| เว็บ         | ใช้ validated contracts, class components และ Context/ServiceContainer ตามเดิม |
| การทดสอบ     | `Portfolio.Checks` และ browser fixtures                                        |
| ขอบเขตบริการ | Team/MarketData ไม่มีสิทธิ์เขียน position หรือคิวของ Portfolio                 |
| การแยกไฟล์   | ทุก named type ยังแยกไฟล์ตามกฎ repository                                      |

รายละเอียด Portfolio:

- แยกความรับผิดชอบของ `TradingReportDelivery`
- เปลี่ยนเฉพาะ delivery/report timer ใน `TradingWorker`
- เพิ่ม Hangfire adapters/registration ใน Infrastructure/Api
- เพิ่ม exit plan ใน Domain/Application เดิม

## 4. งาน A: Telegram delivery ผ่าน Hangfire

### 4.1 เส้นทางข้อมูลและการกู้คืน

1. ยืนยัน fill และบันทึก alert ใน canonical SQL transaction เดิม โดยมี identity ที่รวม account และ fill/intent ที่ชัดเจน การนำเข้า fill เดิมซ้ำไม่สร้าง notification ใหม่
2. หลัง commit ขอ enqueue แบบ bounded นอก execution gate ความล้มเหลวของ Hangfire ไม่ทำให้ fill สูญหาย ไม่ทำให้ broker ถูกส่งซ้ำ และไม่ขวางการป้องกัน position
3. มี recovery sweep ทุกหนึ่งนาทีเพื่อค้น `pending` ที่ยังไม่ได้ประมวลผล รองรับช่องว่างระหว่าง business commit กับ enqueue โดยมี dispatch metadata/backoff แบบถาวร ป้องกันคิวงานซ้ำสะสมไม่จำกัด
4. Job อ่าน account และ destination binding ที่ถูกต้อง แล้ว claim alert เดียวจาก `pending` เป็น `sending` แบบ atomic ก่อน Telegram HTTP ใช้ idempotency key เดิมเสมอ งานซ้ำที่ claim ไม่ได้จบแบบ no-op
5. ปล่อย account lock ก่อนเรียก HTTP เมื่อได้รับ positive acknowledgement พร้อม message ID จึงบันทึก `sent` และ receipt; ถ้าเก็บ receipt ไม่สำเร็จให้ retry เฉพาะการเก็บผล ไม่เรียก `sendMessage` ซ้ำ
6. Timeout, response ที่ยืนยันผลไม่ได้ หรือ process ตายหลัง claim ให้ถือว่า uncertain และคง `sending`/`unknown` เพื่อการตรวจ ไม่เปลี่ยนกลับ `pending` จากอายุ lease หรือ TTL อัตโนมัติ
7. เก็บสถานะ delivery แยกจากสถานะ Hangfire ชัดเจน `Succeeded` อาจหมายถึง no-op หรือบันทึก Unknown สำเร็จ ไม่ได้แปลว่าผู้ใช้ได้รับข้อความ

ห้ามเขียนสถานะ `queued` แล้วทำให้ข้อความหายเมื่อ enqueue ไม่สำเร็จ และห้ามหวังพึ่ง transaction ร่วมกับ Hangfire โดยไม่ได้พิสูจน์ว่า provider รองรับจริง เลือก outbox/recovery เป็นฐานหลัก

### 4.2 Retry และลำดับข้อความ

- ปิด automatic exception retry ของงานที่ส่งข้อความ แต่ยังออกแบบรองรับ job ถูกเรียกซ้ำจาก shutdown/recovery เสมอ `Attempts = 0` ไม่ใช่ exactly-once guarantee
- Retry ได้เฉพาะความล้มเหลวก่อน side effect หรือผลตอบกลับที่ยืนยันว่าไม่รับข้อความ เช่น rate limit ที่มี `retry_after`; แยกจาก 5xx/timeout ที่อาจส่งสำเร็จไปแล้ว กำหนดเพดานและเวลาลองใหม่ก่อน implement
- การ retry หลัง known rejection ต้องเก็บหลักฐานและสิทธิ์ลองใหม่แบบ CAS ก่อนคืน pending; ถ้าเก็บไม่สำเร็จคง uncertain ส่วน configuration/recipient rejection ที่แก้เองไม่ได้ให้แสดง Needs review และหยุดลอง ไม่วน retry ไม่สิ้นสุด
- Queue dispatch และงานอ่าน/เตรียมรายงานที่ idempotent มี bounded retry ได้ แต่ไม่มีทาง retry broker POST ผ่าน Hangfire
- เลือกคิวตามเวลา fill และ identity ที่คงที่ ส่ง oldest eligible ก่อน พร้อมเวลา fill จริง ข้อความล่าช้าต้องระบุว่าเป็นรายการย้อนหลัง ไม่อ้างว่าเพิ่งซื้อ/ขาย
- partial fills ใช้ identity ต่อ fill และแสดงจำนวนที่ยืนยันจริง ไม่สรุปเป็น filled ทั้ง order หรือรวมจนค่าธรรมเนียม/ส่วนที่เหลือสูญหาย
- สถานะเดิมที่ `sending`/`unknown` รวมถึงรายการจากก่อน migration คงไว้ การทดสอบห้ามแก้เป็น pending เพื่อให้คิวดูสะอาด
- การเปลี่ยน account/chat/bot configuration ต้องตรวจ destination binding ก่อนส่ง ห้ามปล่อยคิวของบัญชีเดิมไปยังปลายทางใหม่โดยเงียบ ๆ

### 4.3 รายงาน Dashboard และข้อความ

- แยกเตรียม/แก้ไข daily report ออกจากส่ง alert ใช้ Bangkok business date และ identity `(account, date, mode, revision)` เดิม รักษา catch-up และการแก้รายงานเมื่อ fill มาช้า
- Adaptive Telegram ส่งเฉพาะข้อมูลซื้อ/ขายที่ยืนยัน fill ตามที่เจ้าของต้องการ รายงานประจำวัน/ข้อวิจัย/ความผิดปกติแสดงในแอป ไม่เพิ่มการขอ Approve ใน AutoLive
- ข้อความภาษาไทยสั้น มี emoji เล็กน้อย: ซื้อหรือขายอะไร จำนวนเท่าไร ราคาจับคู่จริง ยอดเงินและเวลา ค่าธรรมเนียม/กำไรสุทธิเฉพาะที่ยืนยันได้; Unknown ไม่แสดงเป็นศูนย์
- Daily Brief มีสถานะย่อ `Notifications pending`, `Delayed` หรือ `Needs review` พร้อมจำนวน/อายุรายการล่าสุด รายละเอียดเปิดดูเมื่อจำเป็น ไม่เพิ่ม dashboard ยาวในหน้าเทรด
- Hangfire Dashboard สำหรับผู้ดูแลผ่าน loopback และการตรวจสิทธิ์ที่ชัดเจน เริ่มเป็น read-only ไม่เผย arguments/secrets ไม่เปิด bulk retry/delete ของ uncertain delivery หรือ execution
- ค่าเป้าหมายสำหรับทดสอบ: เมื่อบริการและ Telegram พร้อม alert ใหม่เริ่มประมวลผลภายใน 10 วินาที p95; งานตกหล่นจาก enqueue ถูกพบภายใน 90 วินาที; ค้างเกิน 60 วินาทีแสดง Delayed แยก outage ออกจากการทำงานปกติ
- H0 วัดและตรึงเกณฑ์ p95/p99 ของ realtime decision/SQL contention ก่อนเพิ่ม workers ถ้า H ไม่ผ่านเกณฑ์ให้แก้ concurrency/แยก process แล้วทดสอบใหม่ ไม่ยอมให้ protective path ช้าลงเพื่อให้คิวข้อความเร็วขึ้น อายุเก็บ Hangfire jobs ไม่ลบ canonical receipt/unknown หรือเปลี่ยน delivery truth

### 4.4 ชุดทดสอบและเกณฑ์จบ A

- [ ] เขียน regression ให้ล้มก่อน: concurrent jobs, SQL contention, restart ก่อน enqueue, ตายหลัง claim, ส่งสำเร็จแต่ receipt commit ล้มเหลว
- [ ] Fake Telegram ครอบคลุม success, 429, 5xx, malformed response, timeout และ cancellation; ตรวจจำนวน HTTP send จริง ไม่ตรวจเพียงสถานะ job
- [ ] alert เดียวถูก enqueue ซ้ำหลายงานมี accepted send อย่างมากหนึ่งครั้งจาก durable claim; known rejection อนุญาต request ใหม่เฉพาะตามหลักฐาน ส่วน legacy uncertain ไม่เกิด HTTP send เพิ่ม
- [ ] Account isolation, destination rotation, partial fills และข้อมูลล่าช้ายังผูกกับรายการที่ถูกต้อง ไม่ใช้สถานะเดียวกลบทั้ง order
- [ ] Scheduler เก่ากับใหม่เปิดพร้อมกันระหว่าง rollout ไม่ส่งซ้ำ; ใช้ ownership switch ที่ชัดเจน และทดสอบ rollback ขณะงานกำลังส่ง
- [ ] คิวล้น/SQL หรือ Telegram ล่มไม่บล็อก realtime execution; วัดความหน่วงของ protective decision เทียบ baseline ภายใต้โหลดเดียวกัน
- [ ] Daily report date/mode/correction และ adaptive suppression ผ่าน regression; ManualReview callback ยังตรวจ owner/account/order/message ตามเดิม
- [ ] SQL integration ใช้ฐานทดสอบแยกและไม่ต้องมี broker secrets; CI ใช้ fake transport เท่านั้น
- [ ] ติดตั้งแล้วเก็บข้อมูลต่อเนื่อง 72 ชั่วโมง: ไม่มี application duplicate send, แยก pending/unknown ได้ครบ, restart แล้วสถานะคงเดิม และ latency ตามเป้าในช่วง healthy; outage ไม่ถูกนับว่าผ่าน

## 5. งาน B: Adaptive Exit ต่อ position

### 5.1 Version และแผนที่บันทึกถาวร

เพิ่ม policy รุ่นใหม่ เช่น `adaptive-intraday-v3` โดยคงการเลือก entry/ตลาด/sizing ของ v2 ในการทดลองแรก เปลี่ยนเฉพาะทางออก ห้ามแก้ความหมาย/hash/JSON ของ mandate v1/v2 เดิมหรือ upgrade position โดยอัตโนมัติ

- Initial protective stop และ sizing snapshot ของ v2 คงเดิมในการทดลองนี้
- การเปลี่ยนจุด invalidation ตอนเข้าให้กว้างขึ้นต้องเป็น candidate แยกที่คำนวณ sizing ใหม่ด้วย ไม่แอบเพิ่มความเสี่ยงให้ position เดิม
- การเพิ่ม fields/version ต้องใช้ version-aware hash serialization เพื่อให้ payload/hash เดิมเหมือนเดิมจริง ไม่ปล่อย default fields ใหม่ไหลเข้า hash v1/v2

| จังหวะ                | ข้อมูลที่เก็บ                                                              |
| --------------------- | -------------------------------------------------------------------------- |
| ก่อนส่ง entry         | เก็บ plan snapshot ที่ตรวจสอบได้ตามรายการด้านล่าง                          |
| เมื่อ fill ยืนยันแล้ว | ผูก remaining quantity/ต้นทุนจริง/entry fees และ anchor เวลาเข้ากับแผนเดิม |

ข้อมูลใน plan snapshot ก่อนส่ง entry:

- account, intent, rules hash
- evidence/as-of, timeframe ที่ใช้เข้า, setup/regime และนิยาม ATR
- invalidation, target behavior และ trailing activation
- review cadence และ hard holding deadline
- ต้นทุนโดยประมาณ

- หนึ่ง active exit plan ต่อ owned position มี revision/CAS และ order ผูก plan revision ที่ใช้
- ห้ามสร้างเหตุซื้อย้อนหลังหรือย้ายไปกรอบเวลายาวขึ้นเพื่อเลี่ยงขาดทุน
- Partial fill, top-up/deposit, refresh, restart หรือแผน revision ใหม่ไม่เริ่มนาฬิกาถือใหม่
- ไม่รับ imported/manual holding เป็นกลยุทธ์ owned โดยอัตโนมัติ

| ค่าใน UI                                                                | ผู้กำหนด / หลักฐาน                                                                         |
| ----------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| exposure, risk budget, daily loss, spread/slippage/freshness และ expiry | ค่าควบคุมที่เจ้าของอนุญาต แยกจากค่าที่ engine คำนวณ                                        |
| stop, trailing, review และ target                                       | ค่าที่ engine คำนวณ                                                                        |
| ค่าธรรมเนียม                                                            | ใช้หลักฐานจริงหรือ conservative model ที่ระบุว่าเป็นประมาณ ไม่ให้ AI เดาส่วนลดค่าธรรมเนียม |

### 5.2 หลักการตัดสินใจ

| เหตุการณ์                 | พฤติกรรมรุ่นใหม่                                                                                     | สิ่งที่ห้ามเกิด                                            |
| ------------------------- | ---------------------------------------------------------------------------------------------------- | ---------------------------------------------------------- |
| แตะ protective stop       | ประเมินจาก fresh executable quote ทันที ภายใต้ mandate ที่ยังใช้ได้                                  | รอ AI/ข่าว/แท่งปิดเพื่อยกเลิกการป้องกัน                    |
| โครงสร้างหรือสมมติฐานเสีย | ยืนยันจาก completed native candle ตามกฎที่ตรึงไว้ แยกจาก hard stop                                   | ใช้แท่งที่ยังไม่ปิดหรือสร้างแท่งแทนช่องว่าง                |
| ถึงเวลาทบทวน              | ตรวจแนวโน้ม ความคืบหน้า ต้นทุน สภาพคล่องและอายุข้อมูล แล้วเลือก Hold หรือ Exit พร้อมเหตุผล           | ครบ 120 นาทีแล้วขายทุกกรณี หรือถือต่อเพียงเพราะไม่มีข้อมูล |
| ถึงเป้ากำไรในตลาดมีเทรนด์ | ทดลองใช้เป็นจุดปรับ protection/ติดตามเทรนด์ แทนขายทั้งจำนวนทันที                                     | ยกเป้าหนีไปเรื่อย ๆ หรือทำให้ effective stop ถอยลง         |
| ถึงเป้ากำไรในตลาดแกว่ง    | ขายตามกรอบที่ตรึงหลังหักต้นทุน เมื่อ room ถึงแนวต้านไม่คุ้มถือต่อ                                    | ใช้เปอร์เซ็นต์เดียวกับทุกเหรียญโดยไม่ดูบริบท               |
| ได้กำไรพอเริ่ม trailing   | ตรวจ net liquidation value หลัง entry/exit fee และ slippage buffer; เริ่ม trailing ตาม ATR/โครงสร้าง | เริ่มเพียงเพราะราคาสูงกว่าต้นทุนเล็กน้อยแต่สุทธิยังติดลบ   |
| ถึงเวลาถือสูงสุด          | ออกจากสถานะตาม execution protocol เมื่อมีข้อมูล/สิทธิ์พร้อม และคง deadline หากยังส่งไม่ได้           | ต่ออายุเองทุกครั้งที่ AI บอก Wait หรือหลัง restart         |

ลำดับความสำคัญต้องตรึงและทดสอบ:

1. authority/unknown-order checks ก่อน side effect
2. protective stop ก่อนเหตุผลถือ
3. hard deadline ก่อนการขยายเวลา
4. structural invalidation
5. armed trailing
6. regime target
7. scheduled review

หนึ่ง evaluation สร้างได้อย่างมากหนึ่ง order intent

- ระยะ stop/trailing ต้องมีเพดานตาม risk budget ของแผนใหม่
- ระหว่างถือ `effective stop` ของ long position ลดลงไม่ได้ แม้ ATR ขยายหรือเปลี่ยน regime; trailing ที่เปิดแล้วไม่กลับเป็นปิด
- การประเมินจากกรอบบริบทต้องใช้ข้อมูลที่พร้อมจริง ณ เวลานั้น และเปลี่ยน target ได้เฉพาะกฎ/จำนวนครั้ง/เพดานที่บันทึกใน policy

### 5.3 เวลา ข้อมูลขาด และขอบเขต AI

- แยก `ReviewAt`, `NextReviewAt` และ `HardExitAt` ถาวร ใช้ UTC ภายในและแสดง Bangkok ในแอป Review cadence สัมพันธ์กับ entry timeframe; hard deadline anchor จาก first confirmed entry fill ไม่เกินเวลาถือสูงสุดของ policy
- การถือต่อที่ review ต้องมี fresh required candles/quote, setup ยังไม่เสีย, ความคืบหน้าที่นิยามไว้ และ remaining horizon/room หลังต้นทุนเพียงพอ ทุก extension มีเหตุผลและจำนวนครั้งจำกัด
- ทุก profile มี hard holding limit เป็นเวลาจำกัดที่ตรึงใน policy และอยู่ภายใน mandate; ไม่มีค่า unlimited ถ้า regime ประเมินไม่ได้ให้ใช้ fallback ที่ตรึงไว้ ไม่เดาว่าเป็น trend เพื่อถือต่อ
- ถ้าแท่งสำหรับ review ขาด ห้ามขยายอายุ/ลด stop; คง protection เดิมและ review pending โดยไม่เลื่อนนาฬิกา ถึง hard deadline ยังต้องรักษาความต้องการออก หาก quote stale ห้ามส่งราคาที่เดา ให้แสดง degraded และรอข้อมูลสดโดยไม่ reset deadline
- AI/ข่าวล่มไม่ทำให้ engine หยุดเฝ้า position ข้อเสนอจาก AI บันทึกเป็น research เท่านั้นในรุ่นนี้ ไม่เปลี่ยน hard stop, mandate, hash หรือสร้าง order
- ถ้าต้นทุนจริง/ค่าธรรมเนียมยัง Unknown ห้าม arm trailing จากกำไรที่สมมติขึ้น ใช้ protection/deadline ที่ commit ไว้ต่อและแสดงข้อจำกัด ไม่ทำให้สถานะติดค้างโดยไม่มี hard deadline
- Disable, expiry และ Emergency stop ยังคงห้าม new broker POST แม้มี deadline ที่ค้าง ต้องแสดงว่าการป้องกันโดยแอปหยุดแล้วและคำสั่งที่ฝากไว้อาจยังเปิดอยู่ ไม่แอบเปิดสิทธิ์เฉพาะขายหลัง disarm
- อย่ารวม ATR ของ v2 กับสูตรใหม่อย่างเงียบ ๆ ปัจจุบันใช้ค่าเฉลี่ย true range ของ 20 แท่ง; ถ้าทดลอง Wilder smoothing ให้เป็น candidate/version แยกและบันทึกสูตร/period อย่างชัดเจน ATR ใช้ความผันผวน ไม่ใช่หลักฐานทิศทางด้วยตัวมันเอง

### 5.4 ตรึงพารามิเตอร์ก่อนทดสอบผลตอบแทน

E0 ต้องส่งมอบตาราง Parameter Freeze ที่ระบุหน่วย สูตร แหล่งข้อมูล และ fallback ครบก่อนเลือกค่าจากผลตอบแทน:

- regime classifier และ required timeframe
- ATR formula/period/multipliers และ net-profit activation buffer
- review cadence, minimum progress และ extension limit
- target cap และ hard holding limit

เริ่มด้วย candidate ไม่เกิน 3 ชุด:

| ชุด | Candidate                                      |
| --- | ---------------------------------------------- |
| 1   | baseline v2                                    |
| 2   | review-based exit ที่คง target เดิม            |
| 3   | review-based exit พร้อม regime target/trailing |

- ทุกชุดคง entry/sizing/risk และข้อมูลเข้าเดียวกัน
- ไม่เปิด Cartesian search ขนาดใหญ่
- ไม่เลือกค่าจากการทำให้ดีลล่าสุดบางรายการดูดีขึ้น

ตัวเลข 120 นาที/2%/1% ใน UI เดิมไม่ถูกยกเป็นค่าเหมาะสมสากล และแผนนี้ไม่เลือกตัวเลขใหม่ว่าเพิ่มกำไรแน่นอน ค่าที่ตรึงสำหรับทดลองเป็นส่วนหนึ่งของ policy hash ก่อน live activation ไม่ใช่ knob ที่ model ปรับเองทุกข้อความ

### 5.5 Execution และสถานะเก่า

- Reuse durable intent, precision/minimum order checks, available balance และ read-only reconciliation ของ Portfolio แยกการเลือก Sell ออกจากหลักฐาน Filled
- การเปลี่ยน exit plan ไม่แก้ราคาหรือจำนวนของ order ที่ส่งไปแล้ว ต้อง inspect partial fill และ cancel acknowledgement ก่อนสร้างคำสั่งแทนสำหรับจำนวนที่เหลือ; unknown cancel/submit บล็อกการส่งทับ
- เป้าขายในรุ่นแรกเป็น engine-managed trigger ไม่เพิ่ม resting take-profit/OCO ในงานนี้ UI ต้องบอกตามจริง การวางขายรอเป็น phase แยกในแผนข่าวพร้อม cancel/fill race acceptance ของตัวเอง
- Owned positions/intents เดิมใช้ original v1/v2 plan และเวลาของตนเอง ถ้า state เดิมไม่พอ ให้ใช้ legacy path ที่ชัดเจน ไม่ประดิษฐ์ thesis หรือยืดเวลาย้อนหลัง
- การใช้ v3 ครั้งแรกต้องไม่มี owned legacy position/open/uncertain order ที่ policy ใหม่ไม่สามารถดูแลอย่างเข้ากันได้ หากยังมีให้จัดการตามแผนเดิมก่อน ไม่สั่งขายเพื่อให้ migration ผ่าน
- การใช้นโยบายใหม่เป็น material change ต้อง disarm และให้เจ้าของ review/enable ในแอป ภายใน mandate ใหม่ engine คำนวณแผนต่อ position ได้เองโดยไม่ต้องอนุมัติทุก review

### 5.6 TDD และการวัดผล B

- [ ] Fake time: ครบ review แล้วยังมีแนวโน้ม/ความคืบหน้าจึง Hold ได้; setup เสีย, ไม่คืบหน้า, เกิน extension limit หรือ hard deadline จึง Exit ตามกฎ
- [ ] Hard stop ทำงานระหว่างแท่งและไม่รอ AI; fresh quote แต่ history outage ยังใช้ protection เดิมได้; stale quote ไม่ส่งคำสั่งมั่วและไม่เลื่อน deadline
- [ ] ATR spike/ราคาไส้เทียน/หลาย timeframe ไม่ทำให้ stop ถอย, trailing disarm, ใช้อนาคต หรือสลับ timeframe เพื่อหนี invalidation
- [ ] กำไรขั้นต้นบวกแต่สุทธิติดลบยังไม่ arm trailing; tests ตรวจ entry fee, exit fee, partial quantity และ Unknown cost อย่างถูกต้อง
- [ ] สูตร net liquidation ระบุว่า cost basis รวม fee แล้วหรือยังและไม่บวกซ้ำ; dust/minimum order ที่ขายไม่ได้ยังแสดง remaining holding ไม่สรุปว่า Closed
- [ ] Partial fills, duplicate events, manual balance changes, cancellation race, SQL conflict, shutdown และ restart ไม่เพิ่ม quantity/intent ซ้ำหรือ reset เวลา
- [ ] v1/v2 hash/JSON/mandate/entry/exit fixtures เหมือน baseline ทุกประการ; v3 state ไม่ถูก legacy binary ยอมรับแล้วใช้ fallback ผิดรุ่น
- [ ] Backtest คู่จาก entry เดียวกันเพื่อแยกผลทางออก และ portfolio replay ที่ทุน/คิว/positions แย่งกันจริงเพื่อวัดผลการใช้งานรวม
- [ ] ใช้เฉพาะข้อมูลที่มี ณ เวลาตัดสินใจ แยก training/holdout ตามเวลา รวม spread, fee, slippage, liquidity, partial/no fill และความเสี่ยง LIMIT ขายไม่ออก ไม่สมมติจับคู่ตรง wick ทุกครั้ง
- [ ] รายงาน net P/L, expectancy, drawdown, turnover/fees, holding time, เหตุผลออก, unrealized และ open/unknown samples ครบ; MFE/MAE ใช้ประเมินย้อนหลังเท่านั้น ไม่เป็นสัญญาณซื้อขาย
- [ ] เปรียบเทียบเงินจมและโอกาสที่พลาดด้วย benchmark สมมติแยกจากกำไรจริง เงินฝากไม่เพิ่มผลตอบแทนกลยุทธ์แม้นับรวมเป้าพอร์ต

เกณฑ์เลือกรุ่นต้องเขียนก่อนดู holdout:

| เกณฑ์                                 | ผลที่ต้องได้                                                                 |
| ------------------------------------- | ---------------------------------------------------------------------------- |
| safety/authority/duplicate violations | ต้องเป็นศูนย์                                                                |
| drawdown                              | ไม่เกิน risk budget                                                          |
| net expectancy หลังต้นทุน             | ต้องเป็นบวกและไม่ด้อยกว่า baseline บนชุดที่ตรึง พร้อมรายงานช่วงความไม่แน่นอน |
| ผลยังแยกจาก noise ไม่ได้              | Insufficient evidence                                                        |
| ผลด้อยกว่า                            | Reject                                                                       |

- ห้ามเลือกเพราะถือนานขึ้นหรือเสียค่าธรรมเนียมน้อยลงเพียงอย่างเดียว
- ห้ามแก้ parameter หลังเห็น holdout แล้วรายงานว่าเป็นการทดสอบเดิม

1. ใช้ข้อมูลย้อนหลังที่มีเพื่อคัดกรองก่อน
2. เก็บ prospective shadow อย่างน้อย 14 วันและ 30 paired closed outcomes ครอบคลุม trend/range พร้อม stress cases
3. หากไม่พอให้สรุป `Insufficient evidence`

- เกณฑ์ขั้นต่ำนี้ใช้ควบคุมกระบวนการ ไม่ใช่หลักฐานรับประกันกำไรหรือความเชื่อมั่นทางสถิติ
- การผ่าน build/tests แยกจากการผ่านเกณฑ์ใช้เงินจริงชัดเจน

ช่วงเก็บหลักฐานอาจเลย `deadlineExclusive` ของ objective จึงไม่อ้างว่า policy ใหม่จะทำให้ถึง `targetThb` ทันกำหนด และไม่ลดเกณฑ์เพื่อไล่ยอด Shadow ไม่บังคับให้บัญชีถือเงินสด: v2 เดิมทำงานต่อได้ภายใต้ mandate ที่เจ้าของเปิดเองหลัง maintenance โดยไม่รับคำสั่งจากผลทดลอง

## 6. UI ที่ลดงานของเจ้าของ

- Trading status ยังคงมาก่อนรายละเอียด เปิดมาเห็น mode, balance, position, order และ freshness ตาม DESIGN.md
- Settings เก็บขอบเขตทุน/ความเสี่ยง/expiry ที่เจ้าของอนุญาต แสดงส่วนที่ derive อัตโนมัติเป็นสรุปอ่านอย่างเดียว แทนฟอร์มที่ชวนให้ต้องเดาทุกค่า และแสดงฟอร์มเดิมได้เฉพาะ legacy policy
- แต่ละ position มีบรรทัดย่อ: `Holding · Trend intact`, ราคาซื้อจริง, protection ปัจจุบัน, target behavior, `Next review`, `Maximum hold until` และเหตุผลล่าสุด ขยายดู evidence/revision ได้เมื่อจำเป็น
- แยก `Exit triggered`, `Order submitted`, `Partially filled`, `Filled`, `Unknown` และ `Trading disabled` ห้ามใช้คำว่า Sold ตั้งแต่เพิ่งเลือก Sell
- คง app labels ภาษาอังกฤษ ข้อความ Telegram ภาษาไทยสั้น ไม่ส่งการทบทวนทุกครั้งหรือข้อมูลเทคนิคทั้งแผน
- ตรวจ keyboard, loading/stale/unknown/error, date/time และ desktop/tablet/phone โดยไม่ทำให้ Daily Brief ต้อง scroll เพิ่ม

## 7. ลำดับทำงานและเกณฑ์ส่งมอบ

| ช่วง  | ขอบเขต                                                                            | หลักฐานจบ                                                                             |
| ----- | --------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| H0    | baseline, data ownership, dependency pin, delivery contract, retry/cutover design | fixtures และ rollback manifest ระบุ canonical runtime ถูกชุด                          |
| H1    | Hangfire Telegram จาก outbox เดิม                                                 | failure/restart/concurrency tests ผ่าน ไม่มี broker POST                              |
| H2    | report preparation แยก, Dashboard, สถานะย่อ                                       | message/report semantics และ UI regression ผ่าน                                       |
| H3    | ติดตั้ง scheduler เท่านั้นและสังเกต 72 ชั่วโมง                                    | queue/latency/uncertainty แยกได้ ไม่เปลี่ยน rules hash หรือกลยุทธ์                    |
| E0    | Parameter Freeze และ contract ของ exit plan                                       | ลำดับ action, costs, data gaps, review/deadline/fallback ตกลงเป็น deterministic rules |
| E1–E2 | policy/domain/storage/API/UI พร้อม TDD                                            | legacy compatibility, money path, precision/race/restart checks ผ่าน                  |
| E3    | paired replay และ prospective shadow                                              | Accept/Reject/Insufficient evidence พร้อมผลหลังต้นทุนและรายการที่ยังเปิด              |
| E4–E5 | UI สรุปและ compatible deployment                                                  | สำรอง/rollback/reconciliation ผ่าน คง AutoLive ปิด เจ้าของเปิดภายหลัง                 |

- หลัง E2 ผ่าน อัปเดตแบบ additive เพื่อรัน shadow evaluator อ่านอย่างเดียวได้ก่อนจบ E3 โดยยังไม่เปิด v3 และรักษาทางดูแล position เดิมไว้
- ก่อน maintenance ยังต้องปิด AutoLive/สำรอง
- การกลับมาเปิด v2 เป็นการตัดสินใจของเจ้าของ ไม่มีการ restore live authority อัตโนมัติ

- H3 กับ E3 เก็บผลคู่ขนานได้เมื่อไม่แย่ง quota/ทรัพยากร แต่ห้ามเปลี่ยน scheduler กับ live strategy ใน rollout เดียว
- การทดสอบ E เริ่มได้โดยไม่รอให้ข่าวทุกแหล่งผ่าน pilot และไม่ขยายสิทธิ์จากการทดลอง
- การ implement เสร็จแต่ E3 ยังไม่ครบให้รายงานว่า Software verified / Strategy unproven ไม่ทำเครื่องหมายว่า live-ready

### งานที่เหมาะย้ายตามหลัง

| งาน                         | เจ้าของ    | เงื่อนไขก่อนย้าย                                                                                                                                  |
| --------------------------- | ---------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| Portfolio sync/import       | Portfolio  | รักษา startup/request coalescing; ไม่ตั้งรอบใหม่โดยพลการ แยกทุนสดจาก history import ช้า และใช้ account claim ถาวร                                 |
| Daily historical study      | Portfolio  | unique claim ต่อ account/date/rules/data cutoff, จำกัดเวลา/โควตา และไม่รันย้อนหลังสะสมจนขวาง realtime                                             |
| News polling ทุก 5 นาที     | MarketData | คง per-source retry-after, revisions/as-of, outage semantics และฐานของ MarketData                                                                 |
| AI hourly/review scheduling | Team       | รักษา Stop/Resume, signal/deadline/cooldown, research budget และ run identity; งานเก่าต้อง recheck account/enabled/due ก่อนเริ่ม                  |
| AI run execution            | Team       | ออกแบบ lease/startup recovery ใหม่ก่อนเพิ่ม host เพราะปัจจุบัน startup mark active runs Failed; ห้าม restart เรียก model ซ้ำหรือใช้ budget ซ้ำเอง |

งานแต่ละแถวเป็นรุ่นแยกหลัง H3 ผ่าน ไม่จำเป็นต่อการส่งมอบ Telegram และ Adaptive Exit รุ่นแรก Recurring cron ระดับนาทีไม่แทน websocket/quote decisions หรือ callback listener

## 8. Deployment, rollback และการตรวจสุดท้าย

1. รัน npm check/build, format checks, solution build, deterministic/architecture/security checks และ UI ตามขอบเขตจริง การทดสอบทั้งหมดใช้ fake brokers; SQL/Telegram จริงเป็น opt-in ที่แยกจาก CI และไม่ส่งคำสั่งเงินจริง
2. ก่อน maintenance ตรวจ canonical Docker project/account, AutoLive ปิดจริง, owned positions/open/unknown orders และแจ้งสภาพ protection ระหว่างหยุดบริการ ไม่บังคับ cancel/sell เพื่อให้ติดตั้งได้
3. สำรอง canonical SQL ทุกฐานที่เกี่ยวข้องด้วย checksum/verify เก็บ image/config manifest และทดสอบ restore ในฐานแยก คง `.env` และ secrets นอก repository
4. Migration แบบ additive ตรวจ readers/writers ของ mixed versions และเปิด scheduler ใหม่ด้วย ownership switch ป้องกันทั้ง native task และ container เก่ารันซ้อน นับ Hangfire storage เป็นข้อมูลที่ต้องสำรองด้วย
5. H rollout ต้องไม่เปลี่ยน rules hash/account/mode/mandate และไม่แปลง uncertain delivery กลับเป็น pending E rollout คง v1/v2 hash เดิม เพิ่ม v3 แบบปิดไว้ ไม่เปิดหรือยืด mandate ให้เจ้าของ
6. H rollback: หยุด producer/consumer ใหม่และตรวจ active claims ก่อนคืน scheduler เดิม; คง canonical outbox/receipt และ Hangfire history ห้ามล้างฐานหรือเปิด retry-all
7. E rollback: ก่อนมี v3 state ใช้ baseline ที่ทดสอบแล้วได้ หลังมี v3 intent/position ต้องใช้ compatible recovery build ที่อ่านและดูแล state ใหม่นั้นได้ ไม่ย้อนเป็น binary เก่าที่ไม่เข้าใจ policy ใหม่
8. ตรวจหลังอัปเดต: services healthy, queue/realtime latency, account/history/fills, exact hashes, positions/orders/unknown, duplicate sends และสถานะ disarm เจ้าของเป็นผู้ review/enable policy ใหม่ในแอปเมื่อผ่านเกณฑ์

ไม่ถือว่าการสำรองไฟล์สำเร็จเท่ากับ rollback ปลอดภัย และไม่ถือว่า Hangfire ทำให้ PC ที่ sleep/ดับหรือ network ล่มยังส่งข้อความ/เฝ้าราคาได้ Runtime ต้องเปิดและมี health evidence ต่อเนื่องเหมือนเดิม

## 9. แหล่งอ้างอิงสำหรับการออกแบบ

- Hangfire SQL Server storage/configuration: [Using SQL Server](https://docs.hangfire.io/en/latest/configuration/using-sql-server.html)
- Hangfire มี automatic retry โดยค่าเริ่มต้น ต้องกำหนดตาม side effect: [Dealing with exceptions](https://docs.hangfire.io/en/latest/background-processing/dealing-with-exceptions.html)
- งานอาจถูกเรียกซ้ำและควรส่งเพียง entity identity ใน arguments: [Best practices](https://docs.hangfire.io/en/latest/best-practices.html)
- Recurring scheduler ทำงานเป็นรอบระดับนาทีและต้องมี server เปิดอยู่: [Performing recurrent tasks](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html)
- Lock/throttling ไม่แทน transaction/idempotency และบาง package เป็นเชิงพาณิชย์ รุ่นแรกไม่ต้องเพิ่ม: [Concurrency and rate limiting](https://docs.hangfire.io/en/latest/background-processing/throttling.html)
- Dashboard เปิดเผยรายละเอียด job จึงต้องตรวจสิทธิ์และจำกัดการเข้าถึง: [Using Dashboard UI](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html)
- ATR วัดความผันผวนและใช้ประกอบระยะ stop; สูตรของ candidate ต้องระบุเองให้ตรง implementation: [Fidelity ATR](https://www.fidelity.com/learning-center/trading-investing/technical-analysis/technical-indicator-guide/atr)
- การแกว่งระยะสั้นอาจ trigger stop และราคาที่ได้ไม่รับประกันตาม trigger: [Investor.gov: Stop, stop-limit and trailing stops](https://www.investor.gov/introduction-investing/general-resources/news-alerts/alerts-bulletins/investor-bulletins-15)
