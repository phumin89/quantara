# รายการตรวจ Bitkub ก่อนเปิด Live

จัดทำตามคำขอเจ้าของวันที่ 1 ตุลาคม 2026 ขณะนี้ Live ยังไม่พร้อมส่งคำสั่ง ใช้ข้อมูลคำสั่งเดิมหรือตัวอย่างที่ Bitkub ยืนยันได้ ไม่ต้องสร้างรายการซื้อขายใหม่เพื่อเก็บตัวอย่าง การซื้อขายเงินจริงต้องได้รับอนุญาตแยกต่างหาก

## ความคืบหน้าที่ตรวจวันที่ 3 ตุลาคม 2026

- ตรวจเอกสาร [REST V3](https://github.com/bitkub/bitkub-official-api-docs/blob/master/rest-v3.md): `place-bid.amt` เป็นยอดใช้ซื้อ ส่วน `place-ask.amt` เป็นจำนวนเหรียญที่ขาย ประวัติระบุค่าธรรมเนียมเป็น THB แต่ยังต้องยืนยันความสัมพันธ์กับยอดสุทธิและเครดิตก่อนลงบัญชี
- เก็บผล public `/api/v3/market/symbols` สำหรับ BTC/THB, ETH/THB และ SOL/THB ไว้ใน `work/live-step-2/public-symbols.json` พร้อมเวลาตรวจ ทั้งสามคู่รายงาน `price_step=0.01`, `quantity_step=1`, `quantity_scale=0`, `min_quote_size=10`, `base_quantity_scale=8`, `quote_quantity_scale=2` เป็นค่าที่ API รายงาน ไม่ใช่ข้อสรุปว่า sell ต้องขายครั้งละหนึ่งเหรียญ หรือยอดขั้นต่ำรวมค่าธรรมเนียมแล้ว
- พบเอกสาร [Order Update](https://api.bitkub.com/docs/websocket/private/order_update) ที่ระบุยอด executed ฝั่งซื้อรวมค่าธรรมเนียม และแยก total fee, credit used, net fee paid from wallet ส่วน [Match Update](https://api.bitkub.com/docs/websocket/private/match_update) แยกฟิลด์ค่าธรรมเนียมต่อรายการเช่นกัน ข้อมูลนี้ช่วยลดคำถามที่ยังไม่ทราบ แต่ไม่ได้ยืนยันการแปลงฟิลด์ REST หรือการดึงประวัติหลัง reconnect ให้ครบ ตัวอย่าง WebSocket มีราคา 1,000,000, เหรียญรับ 0.005, executed 5,000 และ net fee 12.50 จึงยังใช้ตัวอย่างนี้พิสูจน์สมการบัญชีที่รวมค่าธรรมเนียมไม่ได้
- แก้ wire encoder ให้ปฏิเสธ `BuyBudget` ก่อนเรียกเครือข่าย เพราะ domain กำหนดเป็นวงเงินรวมค่าธรรมเนียม แต่โค้ดเดิมส่ง `Quantity * LimitPrice` โดยละเลยเงื่อนไขนี้ ไม่เปิดให้ส่งจนกว่าจะยืนยันการแปลงยอด ทดสอบ Live/Bitkub ผ่าน 306/306 ด้วย HTTP จำลอง ไม่มีคำสั่งเงินจริง

ขั้นถัดไป: ยืนยัน REST amount/fee/credit กับยอดกระเป๋าก่อน–หลัง และความหมาย quantity step แล้วสร้าง fixtures สำหรับไม่มีเครดิต ใช้บางส่วน และใช้เต็ม หลังจากนั้นจึงเชื่อม normalized fills และ reconciliation เข้ากับ broker จริง รายการด้านล่างยังเป็นเกณฑ์ก่อนเปิด Live

## สิ่งที่ต้องให้ Bitkub ยืนยัน

ขอคำตอบพร้อมตัวอย่างทั้งฝั่งซื้อและขาย สำหรับคำสั่ง spot แบบ limit ของ BTC/THB, ETH/THB และ SOL/THB:

1. **จำนวนขั้นต่ำและทศนิยม:** `quantity_step` ใช้หน่วยอะไรในแต่ละฝั่ง? `quantity_scale`, `base_asset_scale`, `quote_asset_scale`, `price_step` และ `min_quote_size` ใช้ร่วมกันอย่างไร? ยอดขั้นต่ำรวมค่าธรรมเนียมหรือไม่? ขอคำสั่งขั้นต่ำที่ถูกต้องและตัวอย่างที่ถูกปฏิเสธ ระบบจะไม่ปัดเพิ่มวงเงินแทนเจ้าของ
2. **เงิน เหรียญ และค่าธรรมเนียม:** ยอดซื้อรวมค่าธรรมเนียมหรือยัง? `amount` ในแต่ละรายการคือยอดก่อนหัก ยอดสุทธิ หรือเหรียญที่ได้รับ? `fee` และ `credit` เปลี่ยนยอด THB ที่ถูกหักอย่างไร? ฝั่งขายได้เงินสุทธิเท่าไร และมีการหักค่าธรรมเนียมเป็นเหรียญหรือไม่? ขอกรณีไม่มีเครดิต ใช้เครดิตบางส่วน และใช้เครดิตเต็ม เพื่อป้องกันการหักซ้ำ
3. **จับคู่บางส่วนและรหัสรายการ:** `client_id`, รหัสตอบรับคำสั่ง, `first`/`last`/`parent`, `order_id`/`parent_order_id`/`super_order_id` และ `txn_id` เชื่อมกันอย่างไร? เหตุใดตัวอย่างที่ระบุ `filled` จึงมี `filled` ต่างจาก `total` ทั้งที่ `remaining=0`? ต้องยืนยันเหรียญที่ได้รับจากรายการจริง ไม่คำนวณจากสถานะสำเร็จเพียงอย่างเดียว
4. **ประวัติครบทุกหน้า:** รู้ได้อย่างไรว่าดึง cursor ครบแล้ว? `start`/`end` รวมรายการตรงขอบเวลาหรือไม่? ระหว่างดึงหลายหน้ามีรายการซ้ำหรือหลุดได้ไหม? ขอวิธีเข้าถึงประวัติที่เกิน 90 วัน หรือวิธีเริ่มจากจุดตรวจยอดที่ยืนยันได้ ต้องมีหลักฐานครบก่อนถือว่าบัญชีตรงกัน
5. **ผูกบัญชีและสิทธิ์:** ตรวจได้อย่างไรว่ากุญแจอ่านข้อมูลและซื้อขาย spot เป็นของบัญชีที่ผูกไว้ และตรวจสิทธิ์ที่อนุญาตได้อย่างไร? ไม่ต้องใช้สิทธิ์ถอน โอน margin leverage หรือ short-selling

## ตัวอย่างที่ต้องรวบรวมจากคำสั่งเดิม

ใช้คำสั่งเดิมหรือตัวอย่างจาก Bitkub เปลี่ยนรหัสจริงเป็นรหัสสมมติที่เชื่อมกันได้:

- Public symbol metadata for the three approved pairs.
- Buy and sell: submitted request, placement response, every order-info history entry, all order-history pages, and wallet available/reserved balances before and after.
- Zero, partial and full fee credit; multiple partial fills; terminal fill; partial-fill cancellation; rejected order.
- A timeout after placement where the outcome is unknown, followed by the authoritative lookup that resolves it. Never resend to determine whether it worked.
- A cancellation acknowledgement followed by actual terminal-state and wallet evidence. Acknowledgement alone does not release reservations.

ลบ API key, secret, signature, cookie, CSRF token, รหัสจับคู่ ข้อมูลส่วนตัว และรหัสบัญชีจริงก่อนส่ง เก็บตัวเลขทศนิยม หน่วยเงิน เวลา รูปแบบ cursor และความสัมพันธ์ระหว่างรหัสไว้ตรงต้นฉบับ เก็บตัวอย่างแยกจากกุญแจบัญชี ห้ามใส่กุญแจในแชต หน้าเว็บ log หรือกระบวนการ AI

## เกณฑ์รับงานก่อนเชื่อมระบบจริง

Lead review must confirm source provenance and exact accounting with deterministic fixtures. Test precision/minimum rejection, budget buys at favorable prices, gross/net fee conversion, duplicate fills and corrections, lineage, pagination completeness, unknown outcomes without resend, cancellation races, credential replacement and two stable bound-account observations. Mismatched wallets preserve money/reservations and disarm.

Then complete separately reviewed provisioning, Bangkok-day and flow-adjusted performance baselines, isolated executor registration, protected credentials, trusted TLS, owner limits/controls, restart/recovery and PostgreSQL concurrency checks. Deposits and Sandbox balances never enlarge Live authority. Successful reconciliation never arms automatically. Real canary trading requires explicit owner activation and separate transaction authorization.

## แหล่งข้อมูลที่ตรวจวันที่ 1 ตุลาคม 2026

Official [V3 API documentation](https://github.com/bitkub/bitkub-official-api-docs/blob/master/rest-v3.md) identifies buy `amount` as THB and sell `amount` as crypto. Its symbol example contains `quantity_step="1"` with no field explanation. Its filled order-info example contains `filled=3999.97`, `total=4000`, `remaining=0` and separate fee/credit fields. These examples do not establish every accounting and rounding rule above.

Official [V4 wallet documentation](https://github.com/bitkub/bitkub-official-api-docs/blob/master/rest-v4.md) remains the wallet contract. The official [repository announcement](https://github.com/bitkub/bitkub-official-api-docs) states history older than 90 days is archived. The public transport and existing fake-HTTP tests are not broker-confirmed end-to-end execution evidence.
