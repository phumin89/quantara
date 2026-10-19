# แผนงาน Quantara

## เป้าหมายปัจจุบัน

**ใช้ objective ปัจจุบันที่เซิร์ฟเวอร์ส่งให้สำหรับบัญชีนั้น** โดยอ่าน `targetThb` และ `deadlineExclusive` จาก `objective`/`currentGoal` ตาม policy ที่กำหนดอยู่ วัด equity จากเงินสดและมูลค่าเหรียญรวมเงินเติมทุนที่ยืนยันแล้ว เป้าหมายไม่เพิ่มสิทธิ์ execution และเอกสารนี้ไม่ได้เพิ่มความสามารถแก้เป้าผ่าน UI

- วัดจากยอดเงินจริงและเหรียญที่ถือ ณ ราคาที่ตรวจสอบได้ โดยไม่บวกเงินจองออเดอร์ซ้ำ
- แสดงเงินเติมทุน กำไร/ขาดทุน และช่องว่าง `max(0, targetThb - equity)` แยกกัน; หาก objective หรือ valuation ไม่ครบให้แสดง Unknown
- คงความเข้ากันได้ของฟิลด์เป้า legacy และ rules hash เดิม โดยไม่ใช้ฟิลด์ legacy เป็น objective ปัจจุบันหรือคำสั่งบังคับกำไร
- รายละเอียดเกณฑ์วัดอยู่ในแผนละเอียดข้อ 1.1

## ลำดับถัดไป: Aggressive Portfolio และทีม AI ตลอดวัน

แผนวิจัยและ review วันที่ 19 ตุลาคม 2026: [หลักฐาน กลยุทธ์ งบ และแผน implement](docs/aggressive-portfolio-plan.md)

| ประเด็น     | แนวทาง                                                                                                  |
| ----------- | ------------------------------------------------------------------------------------------------------- |
| พอร์ต       | คง Aggressive ใช้ทุนตามโอกาส ภายใต้ risk/exposure/liquidity caps ไม่มี fixed amount ต่อดีลที่ตั้งลอย ๆ  |
| กลยุทธ์     | เปรียบเทียบ intraday กับ swing 2–60 วัน แยกแผนออกและ ownership; ข่าวเริ่มเป็น research overlay          |
| งบ          | เงินที่ใช้ได้ ความเสี่ยงรวม ค่า data/operation และ AI quota วัดแยกกัน                                   |
| ทีม AI      | แยก specialist/skills เรียกเฉพาะงาน ทำงานข้ามวัน; จำกัดจำนวนงานตาม rolling hour ไม่มี daily cutoff      |
| หลักฐานกำไร | shared-capital replay, chronological holdout และ prospective execution evidence; ไม่ใช้จำนวนดีลแทน edge |
| สิทธิ์      | R4 เพิ่มการจัดงานวิจัย ไม่เปลี่ยน v1/v2/v3, mandates หรือเปิด AutoLive                                  |

- [x] Research แหล่งปฐมภูมิและ review จุดเชื่อมในโค้ด พร้อมแยกข้อเท็จจริง สมมติฐาน และข้อจำกัด
- [ ] R0 ตรึง baseline, coverage/usage audit, trial budget และเกณฑ์ประเมินก่อนดู holdout
- [ ] R1 แก้ study selection/retry และเพิ่ม research-only 4h/1D + long-window history โดยไม่ขยายกฎ execution เดิม
- [ ] R2 เพิ่มข่าวเฉพาะเหรียญ/โบรกเกอร์ที่ตรวจแหล่งและสิทธิ์ได้ พร้อม as-of/revisions
- [ ] R3 เพิ่ม portfolio replay และ aggregate stop-risk budget ใช้ทุนเดียว ไม่จองซ้ำ
- [x] R4 ซอฟต์แวร์: แยกบทบาท/skills/tasks, scoped evidence และ rolling-hour budget พร้อมสถานะพัก/รอบถัดไป; [รายละเอียดและข้อจำกัด](docs/continuous-research.md)
- [x] R4 Deployment: ติดตั้ง Team/Web หลังปิด AutoLive พร้อม canonical backups, isolated restore และตรวจข้อมูลเดิม
- [ ] R4 Runtime pilot: เก็บ usage/latency/provider availability ต่อเนื่อง; ยังใช้ bounded-request fallback ไม่ใช่ token spending cap
- [ ] R5 เปรียบเทียบ intraday/swing/news-overlay หลังต้นทุน บันทึกทุก trial และผลที่ไม่ผ่าน
- [ ] R6 เก็บ prospective evidence ของรุ่นที่ตรึงไว้จนเพียงพอ ไม่ตีความ uptime เป็นกำไร
- [ ] R7 เสนอ policy/hash และ live rollout แยก เฉพาะ candidate ที่ผ่าน; เจ้าของเปิด mandate ใหม่เอง

งานเดิมด้านล่างเป็น retained workstreams เก็บสถานะตามหลักฐานเดิม เกณฑ์ H3/E3/E5 และ pilot ข่าวยังไม่ผ่านเพราะเขียนแผนใหม่ รายละเอียด dependencies และ acceptance อยู่ในแผนที่ลิงก์ข้างต้น

## งานเดิม: Hangfire และ Adaptive Exit

| รายการ     | สถานะ                                                                                |
| ---------- | ------------------------------------------------------------------------------------ |
| แผนละเอียด | [งานเบื้องหลังและแผนขายตามสถานการณ์](docs/background-jobs-and-adaptive-exit-plan.md) |
| การติดตั้ง | ตรวจตาม deployment gates และเก็บหลักฐาน runtime แบบส่วนตัว                           |
| AutoLive   | ต้องปิดก่อน maintenance; สถานะปัจจุบันตรวจในแอป                                      |
| กลยุทธ์    | v2 คงเดิม และ v3 ยังเป็นรุ่นทดลอง                                                    |

ทำสองงานเป็นคนละรุ่นเพื่อวัดผลแยกกัน: เริ่มให้คิว Telegram ตรวจสอบได้ด้วย Hangfire แล้วพัฒนาทางออกที่ทบทวนตามตลาด โดยใช้ objective ของบัญชีและขอบเขตความเสี่ยงที่บันทึกไว้ การเพิ่ม Hangfire ไม่เปลี่ยนกติกาซื้อขาย

- [x] H0 ตรึง baseline/hash/เจ้าของข้อมูล ระบุสถานะคิวเดิมและเกณฑ์ทดสอบก่อนย้าย scheduler
- [x] H1 ใช้ Hangfire + SQL Server ใน Portfolio สำหรับส่งข้อความ fill จาก outbox เดิม มี durable claim และกู้ pending ที่ enqueue ไม่สำเร็จ
- [x] H2 แยกเตรียมรายงานออกจากส่งข้อความ เพิ่มสถานะคิวแบบย่อและ Dashboard สำหรับผู้ดูแล; ตรวจ restart/timeout/งานซ้ำด้วย fake Telegram
- [ ] H3 ติดตั้ง Hangfire โดยไม่เปลี่ยนกลยุทธ์; ต้องมีหลักฐาน latency/งานค้าง/ข้อความซ้ำต่อเนื่อง 72 ชั่วโมง โดยไม่ส่งข้อความเก่าที่ Unknown ซ้ำ
- [x] E0 ตรึงนิยามกรอบเวลา ATR ต้นทุน ลำดับทางออก และชุดพารามิเตอร์ทดลองก่อนพัฒนานโยบายใหม่
- [x] E1 เพิ่ม policy รุ่นใหม่ที่แยกเวลาทบทวนกับเวลาถือสูงสุด คำนวณแผนต่อ position และรักษาจุดตัดขาดทุนที่ไม่ถอยลง
- [x] E2 ทดสอบ TDD ทางออกและวงจรคำสั่ง รวม partial fill, cancel/fill race, stale data, restart และ mandate หมดอายุ
- [ ] E3 เปรียบเทียบ v2 กับทางออกใหม่บนข้อมูลตามเวลาจริงและ holdout; ผลทดลองไม่รวมเป็นกำไรบัญชี และข้อมูลไม่พอให้รายงานตามจริง
- [x] E4 ลดฟอร์มเทคนิคเป็นแผนที่ระบบคำนวณ พร้อมเหตุผลถือ/ขายและเวลาทบทวน; Telegram แจ้งเฉพาะซื้อ/ขายที่ยืนยัน fill
- [ ] E5 ติดตั้ง code หลังปิด AutoLive/สำรองและทดสอบ restore; position เดิมคงแผนเดิม รุ่น v3 ยังห้ามเปิดเงินจริงจน E3 ผ่าน และเจ้าของเป็นผู้เปิดเอง
- [ ] L1 ภายหลัง H3 ผ่าน: พิจารณาย้าย sync/history study/news polling/AI scheduling ทีละงาน โดยรักษาเจ้าของฐานข้อมูล cooldown และงบวิจัย

หลักฐานการตรวจรุ่นนี้อยู่ใน [บันทึกตรวจ](docs/hangfire-adaptive-exit-review.md) และ [Parameter Freeze](docs/adaptive-exit-protocol.md)

- E3 ยังไม่ผ่าน: ต้องเก็บ prospective อย่างน้อย 14 วัน/30 คู่ และทำ historical paired/portfolio replay ก่อนพิจารณาเงินจริง
- v3 รุ่นนี้ยังปิด live activation ไว้
- H3 ยังรอหลักฐานการสังเกต 72 ชั่วโมง

เกณฑ์จบและวิธี rollback ระบุในแผนละเอียด:

- ห้ามถือว่า Hangfire Succeeded เท่ากับ Telegram ส่งสำเร็จ
- ห้ามถือว่าระยะถือยาวขึ้นเท่ากับทำกำไรดีขึ้น
- ไม่เปลี่ยนสิทธิ์ AI ให้ส่งคำสั่งเงินจริงในงานสองชุดนี้

## แผนที่ดำเนินต่อ: ข่าวและการบริหารแผนเทรด

แผนละเอียด: [ข่าว การวิจัย และแผนเทรดของ Quantara](docs/news-aware-trading-plan.md)

| ส่วน                     | สถานะ                                                          |
| ------------------------ | -------------------------------------------------------------- |
| ซอฟต์แวร์ P0/P1          | ตรวจตาม deployment และ pilot gates                             |
| เป้าพอร์ต                | objective และ equity ที่ตรวจสอบได้ภายในบัญชี                   |
| แหล่งข่าว                | แสดง coverage/freshness/Unavailable/UnsupportedSource รายแหล่ง |
| Pilot 72 ชั่วโมง         | ยังไม่ผ่าน                                                     |
| การประเมินแบบมีโครงสร้าง | เป็นรายงานอ่านอย่างเดียว ไม่เปลี่ยนกติกา v2 หรือเปิด AutoLive  |

เป้าหมายคือให้ทีม AI ใช้ข่าวที่ตรวจสอบได้ค้นหาโอกาสและทบทวนเหตุซื้อ ขณะที่ Portfolio ดูแลขอบเขตเงินจริงและ execution แผนแยกการปรับทางออกกับการเพิ่มข่าว เพื่อวัดผลได้ว่าอะไรทำให้ดีขึ้น

- [x] P0 ตรึง baseline v2/rules hash และ binary สำหรับ rollback; regression เข้า/ขาย/เปิดใหม่/partial/unknown ใช้ precision fixtures ที่ไม่อ้างข้อมูลบัญชีจริง
- [x] P0 แยก objective ของบัญชีใน UI/AI/report จาก risk/mandate รวมเงินเติมทุนที่ยืนยันแล้วและคงความเข้ากันได้ของเป้า legacy
- [ ] P1 ข่าวจริงครบเส้นทาง: รับข้อมูล เก็บ revision ส่งเข้า AI และแสดง Daily Brief
  - [x] Feed/storage/HTTP/UI และเพดาน context/รอบ AI แบบถาวร พร้อมทดสอบ SQL จริง, revision/as-of, restart, 304/429 และ source outage
  - [x] Typed assessment ครอบคลุม CEO/Analyst/Reviewer และ evidence/watchlist validation; การทดสอบไม่ส่งออเดอร์จริง
  - [ ] Pilot ต่อเนื่อง 72 ชั่วโมง พร้อมแก้ช่องว่างแหล่งข่าว; ห้ามนับ source outage เป็นไม่มีข่าว
- [ ] P2 ทบทวน position และโอกาสใหม่ด้วย thesis/กราฟ/ข่าว พร้อมเงื่อนไขตรวจใหม่
- [ ] P3 ทดลองทางออกที่แยก protective stop, thesis exit, review และ deadline; ออกแบบขายทำกำไรรอพร้อม cancel/fill reconciliation
- [ ] P4 เทียบ baseline/ทางออก/ข่าวด้วยข้อมูลที่รู้ ณ เวลานั้น รวมต้นทุนและค่าเสียโอกาสที่แยกจากกำไรบัญชี
- [ ] P5 เชื่อมแผน AI กับเงินจริงเฉพาะ policy/mandate รุ่นใหม่ที่ผ่านการตรวจและเจ้าของเปิดเอง
- [ ] P6 ทบทวนผลงานจริงและข้อทดลองทุกวัน โดยไม่มี auto-promotion หรือการขยายสิทธิ์จากข่าว

จุดจบรุ่นแรกคือ P1 ครบเส้นทางและตรวจได้ ยังไม่ใช่ AI เลือกคำสั่งเงินจริง; ความสามารถนั้นมีเกณฑ์แยกถึง P5 ใช้ objective ของบัญชีเพื่อวัดผล ไม่ใช้เป้าหมายบังคับสร้างดีลหรือเพิ่มความเสี่ยง

การตรวจที่ต้องคงไว้ทุก deployment:

- build/check/format/architecture, deterministic checks, SQL แยกสำหรับทดสอบ และหน้าจอ desktop/tablet/มือถือ
- สำรอง canonical SQL ทั้งสามฐานพร้อม checksum และทดสอบ restore ก่อนอัปเดต
- คงประวัติรายงาน บัญชี rules hash และสถานะ disarm; คง private configuration นอก repository

## ประวัติงานรุ่น adaptive-intraday-v2

Checklist ด้านล่างเป็นประวัติ implementation/verification ของรุ่นนี้ ฟิลด์เป้า legacy และ rules hash ต้องคงความเข้ากันได้ ส่วนงานใหม่ใช้ objective ของบัญชีตามส่วนต้นเอกสาร

ขอบเขตการตรวจรุ่น:

- `adaptive-intraday-v2` แยก version/hash และรักษาสิทธิ์รุ่นเดิม
- ตรวจความละเอียดจำนวนทั้งการส่งขายและการเปิด Auto ใหม่ด้วย fake broker
- การอัปเดตต้องสำรอง SQL ทั้ง 3 ฐานและตรวจ restore ก่อน
- สถานะเปิด/ปิดปัจจุบันให้ดู Daily Brief ไม่อนุมานจากเอกสารนี้
- เครื่องหมายเสร็จด้านล่างหมายถึงงาน implementation/verification ไม่ใช่หลักฐานว่ากลยุทธ์ทำกำไรได้

## 1. ทำให้ข้อมูลพร้อมใช้

- [x] ตรวจช่องว่างรายเหรียญ แยก source-unavailable, แท่งที่แหล่งข้อมูลรายงาน volume เป็นศูนย์ และช่องว่างที่ยังไม่ทราบสาเหตุ
- [x] ดึงข้อมูลจาก Bitkub native history เก็บเวลาและที่มา ใช้ช่วงล่าสุดที่เพียงพอ โดยไม่สร้างแท่งราคาแทนข้อมูลที่หาย
- [x] ทุกเหรียญที่ตรวจมีสถานะพร้อมใช้หรือเหตุผลที่ใช้ไม่ได้ พร้อมเวลาตรวจ; ไม่ตีความแท่งที่หายว่าไม่มีธุรกรรม

## 2. ปรับการหาจังหวะตามตลาด

- [x] v2 เลือกกรอบที่สั้นที่สุดตามลำดับ 5/15/60 นาที เมื่อมีอย่างน้อย 51 แท่งต่อเนื่องถึงแท่ง native ล่าสุดที่ปิดแล้ว และยังตรวจ spread/liquidity/risk เดิม
- [x] สแกนทุกคู่ THB ที่เข้าเกณฑ์แบบหมุนเวียน จัดอันดับ Breakout / Pullback / Range จากโอกาสหลังต้นทุนและความเสี่ยง
- [x] ทดสอบ 5 นาทีไม่พร้อมแต่ 15/60 นาทีพร้อม เข้า/ออก/รอ, risk preflight และกันคำสั่งซ้ำ; ทางออกระหว่างแท่งทำงานแยก ไม่บังคับซื้อเพื่อให้ครบจำนวนดีล

## 3. ตรวจว่ากลยุทธ์ทำเงินได้หรือไม่

- [x] ดึงย้อนหลัง 7 วัน ผ่าน MarketData แยกตามเวลา 70/30 training/validation; ข้อมูลไม่ครบเป็น Unknown และแสดง insufficient samples
- [x] รวมค่าธรรมเนียม spread และ slippage; ต้องเป็นแท่งถัดมาที่ผ่านราคา limit อย่างชัดเจน แตะอย่างเดียวไม่ใช่ fill; ผลทั้งหมดเป็น hypothetical
- [x] รายงานดีล net, fees, drawdown และ cash benchmark แยกกลยุทธ์/กรอบเวลา; ทดสอบ no-lookahead, ช่องว่าง, expiry และผลไม่แน่นอนแล้ว

## 4. นำรุ่นที่ผ่านไปใช้งานจริง

- [x] Regression ด้วย fake broker ครอบคลุมเข้า ออก ยกเลิก partial fill และผลคำสั่งไม่แน่นอนที่ห้ามส่งซ้ำ
- [x] v2 มี version/hash แยก คง v1/สิทธิ์เดิม ทดสอบว่า Save รุ่นใหม่ disarm และเก็บยอดเงิน/ประวัติ; เจ้าของเปิด mandate ใหม่เอง
- [x] ตรวจเส้นทางกระทบยอด/แผนออกและ Telegram เฉพาะ confirmed fills ด้วย fake broker; build/lint/format/architecture และ UI desktop/tablet/phone ผ่าน
- [x] มี deployment gates ให้ปิด AutoLive สำรอง SQL ทั้ง 3 ฐาน ตรวจ checksum/fingerprint/restore ก่อนอัปเดต container และตรวจ schema/ข้อมูล/สิทธิ์หลังอัปเดต
- [x] เส้นทาง mandate กำหนดให้เจ้าของเป็นผู้เปิดในแอป; การพัฒนา/ทดสอบ/maintenance ไม่เปิด AutoLive หรือส่งออเดอร์เงินจริง

## 5. ทบทวนและพัฒนาทุกวัน

- [x] Daily Brief แสดง scan readiness และ funnel รายวัน พร้อม objective ของบัญชี; รายละเอียดข้อมูล/ผลย้อนหลังพับเก็บได้
- [x] ส่ง readiness/funnel/historical study และ opportunity review เข้า snapshot ของ AI; แนวทาง CEO/Analyst/Reviewer อธิบายสาเหตุ ไม่ปน hypothetical กับกำไรจริง
- [x] บันทึกผลศึกษารายวันต่อ account/rules hash; ไม่ให้ AI เปลี่ยนหรือ promote กลยุทธ์เอง ข้อเสนอต้องระบุปัญหา ผลเปรียบเทียบและเวอร์ชัน

ข้อจำกัด:

- ชุดศึกษาทดลองไม่ใช่หลักฐานว่าได้กำไรจริงหรือบรรลุ objective ของบัญชี
- ใช้ตัวอย่างสูงสุด 4 เหรียญบน 3 กรอบเวลา; สแกนตลาดจริงยังครอบคลุมทุกคู่ที่เข้าเกณฑ์
- ไม่มีการอนุมาน queue/partial fill จาก OHLC เมื่อหลักฐานไม่พอ

ลำดับงาน: ข้อ 1–2 ก่อน ตามด้วยข้อ 3–5 พัฒนาและทดสอบโดยไม่ส่งคำสั่งเงินจริง; AI คงบทบาทวิจัย การส่งคำสั่งเป็นหน้าที่ deterministic engine ภายใต้ mandate ของเจ้าของ
