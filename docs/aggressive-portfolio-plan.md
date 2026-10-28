# Aggressive Portfolio: Research และแผนพัฒนา

ตรวจแหล่งข้อมูลและโค้ดวันที่ **19 ตุลาคม 2026** เอกสารนี้เป็นแผนวิจัยและพัฒนา ยังไม่ใช่ผลทดสอบกลยุทธ์หรือการเปลี่ยนสิทธิ์เงินจริง ข้อมูลบัญชี ผลประกอบการจริง และหลักฐานปฏิบัติการเก็บแยกจากเอกสารสาธารณะ

## 1. ข้อสรุปที่ใช้วางแผน

| เรื่อง          | ข้อสรุป                                                              | ผลต่อแผน                                                            |
| --------------- | -------------------------------------------------------------------- | ------------------------------------------------------------------- |
| ความพร้อมทำกำไร | ระบบส่งคำสั่งและลงบัญชีได้ ไม่ได้พิสูจน์ว่ามีความได้เปรียบหลังต้นทุน | วัดผลกลยุทธ์กับ execution และบัญชีแยกกัน                            |
| Aggressive      | เปิดโอกาสใช้เงินลงทุนสูงภายใน mandate เมื่อมีโอกาสที่ผ่านเกณฑ์       | ใช้ทุนตามสัญญาณและความเสี่ยงรวม ไม่มีเพดานยอดต่อดีลแบบตั้งลอย ๆ     |
| กรอบเวลาถือ     | กลยุทธ์สั้นไม่ควรถูกใช้แทนสมมติฐานถือหลายสัปดาห์                     | วิจัย intraday และ swing แยกเวอร์ชัน แผนออก และผลลัพธ์              |
| ข่าว            | ต้องเชื่อมเหตุการณ์กับอุปสงค์ อุปทาน หรือความสามารถซื้อขายได้จริง    | เพิ่มแหล่งทางการและตรวจเวลาที่ระบบรู้ข่าว ก่อนใช้ sentiment         |
| การพัฒนาทุกวัน  | เรียนจากผลและความคลาดเคลื่อน ไม่ปรับกฎเงินจริงทุกวัน                 | บันทึกการทดลอง ตรึงรุ่น และประเมินกับข้อมูลที่ยังไม่ใช้เลือกกลยุทธ์ |
| ทีม AI          | แยกงานตามหลักฐานและความชำนาญ เรียกเฉพาะบทบาทที่จำเป็น                | งานเฝ้าตลาดไม่ต้องรอการประชุมครบทีม                                 |

**ขอบเขตเริ่มต้น:** Bitkub THB spot, long-only, ไม่กู้ ไม่ใช้ leverage ไม่เพิ่ม futures/options หรือ Webull ในโครงการวิจัยชุดแรก เป้าหมายพอร์ตอ่านจาก objective ของบัญชี เงินเติมทุนแสดงแยกจากผลตอบแทนการเทรด เป้าหมายไม่เปลี่ยนขอบเขตความเสี่ยงและไม่บังคับให้ซื้อขาย

## 2. Review โค้ด: สิ่งที่มีและสิ่งที่ขาด

| ลำดับ | หลักฐานใน repository                                                                                                                                                                                                                                                                                       | ช่องว่างที่ต้องแก้                                                                                                                                                            |
| ----- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| P0    | [TradingEvaluationWorker](../services/portfolio/Portfolio.Infrastructure/Trading/Evaluation/TradingEvaluationWorker.cs) เลือก 4 เหรียญจากคะแนนแล้วเรียงชื่อ และเก็บผลรายวันตาม hash                                                                                                                        | ไม่กรองความพร้อมประวัติก่อนเลือก; ผลที่ข้อมูลไม่ครบทำให้ไม่ลองใหม่ในวันเดียวกัน ต้องเพิ่ม coverage-aware selection และ retry ที่มีเหตุผล                                      |
| P0    | [TradingEvaluationService](../services/portfolio/Portfolio.Application/Trading/Evaluation/TradingEvaluationService.cs) ใช้ 7 วัน แบ่ง 70/30 และรองรับ v2                                                                                                                                                   | ยังไม่ใช่ portfolio replay หรือการพิสูจน์ความได้เปรียบ ผล sufficient/จำนวนดีลขั้นต่ำเป็นเพียงคุณภาพตัวอย่างเบื้องต้น                                                          |
| P0    | [AdaptiveRiskEngine](../services/portfolio/Portfolio.Domain/Risk/AdaptiveRiskEngine.cs) จำกัด cash/exposure/concentration/stop risk/liquidity                                                                                                                                                              | งบ stop risk คำนวณต่อดีลจาก equity แต่ยังไม่หัก stop risk ของทุกสถานะและ pending entry รวมกัน                                                                                 |
| P0    | [TradingSignalProcessor](../services/portfolio/Portfolio.Application/Trading/Decisions/TradingSignalProcessor.cs) ใช้หนึ่ง position ต่อ symbol                                                                                                                                                             | ยังไม่มี ownership แยก intraday/swing; ห้ามเพิ่มอีกกลยุทธ์แล้วขาย inventory ของกันและกัน                                                                                      |
| P1    | [NewsPoller](../services/market-data/MarketData.Infrastructure/News/NewsPoller.cs) มี Fed/BLS; [SqlNewsStore](../services/market-data/MarketData.Infrastructure/News/SqlNewsStore.cs) มี revisions/as-of                                                                                                   | ข่าวเฉพาะเหรียญและประกาศ Bitkub ยังไม่เป็น feed ที่ยืนยันแล้ว; [Team reader](../services/team/Team.Infrastructure/Portfolio/NewsEvidenceReader.cs) อ่าน packet ล่าสุดเท่านั้น |
| P1    | [CompanyLoop](../services/team/Team.Infrastructure/BackgroundJobs/CompanyLoop.cs) มี hourly review, signal poll 1 นาที, cooldown 15 นาที และ Stop/Resume                                                                                                                                                   | [Budget](../services/team/Team.Application/Company/ResearchBudgetRules.cs) จำกัด 2 รอบ/ชั่วโมง และ 24/32 รอบต่อวัน แทนการจัดงบตามงาน/โทเคน จึงใช้โควตาหมดก่อนจบวันได้         |
| P1    | [TeamCoordinator](../services/team/Team.Infrastructure/BackgroundJobs/TeamCoordinator.cs) สร้าง CEO/Analyst/Reviewer ทุก run; [worker](../services/ai-worker/CodexTeamWorker.mjs) โหลด AGENTS.md และ SKILL.md รายบทบาทอยู่แล้ว                                                                             | Analyst รวมหลายหน้าที่ และทั้งทีมถูกเรียกซ้ำแม้ต้องการงานเฉพาะเรื่อง; ต้องแยก role contract และงานย่อย                                                                        |
| Reuse | [RiskCapitalReader](../services/portfolio/Portfolio.Application/Risk/RiskCapitalReader.cs), [ResearchAssessmentValidator](../services/team/Team.Infrastructure/BackgroundJobs/ResearchAssessmentValidator.cs), [TradingExitStudy](../services/portfolio/Portfolio.Application/Trading/TradingExitStudy.cs) | ใช้ยอดเงิน/เงินจองและ provenance เดิม เพิ่มข้อมูลแบบ additive ไม่ทำ ledger หรือฐานข้อมูลซ้ำอีกชุด                                                                             |

การเก็บ paired exits อย่างน้อย 14 วัน/30 คู่ใน [protocol เดิม](adaptive-exit-protocol.md) เป็นเกณฑ์ **พร้อมทบทวน** ไม่ใช่หลักฐานกำไร เกณฑ์ H3/E3/E5 และ pilot ข่าวใน [plan หลัก](../plan.md) ยังเปิดอยู่ตามหลักฐานเดิม

## 3. Research: หลักฐานรองรับอะไร

| หลักฐานปฐมภูมิ                                                                                                          | สิ่งที่นำมาใช้ได้                                                | ข้อจำกัด                                                                                         |
| ----------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| Liu/Tsyvinski, [Risks and Returns of Cryptocurrency](https://www.nber.org/papers/w24877), working paper 2018 / RFS 2021 | มีเหตุผลให้ทดสอบ momentum และ investor attention                 | ตัวอย่างในอดีตของ BTC/XRP/ETH ไม่ยืนยันกำไร Bitkub THB ปัจจุบันหลังต้นทุน                        |
| Liu/Tsyvinski/Wu, [Common Risk Factors](https://www.nber.org/papers/w25882), working paper 2019 / JF 2022               | ตรวจ market factor และ momentum ร่วมกัน                          | งาน long-short ไม่ใช่ผลของ long-only spot; หลายเหรียญอาจเป็นความเสี่ยงเดียวกัน                   |
| Wen et al., [Intraday momentum and reversal](https://www.sciencedirect.com/science/article/pii/S1062940822000833), 2022 | แยกสมมติฐาน trend/reversal และสภาวะตลาดไว้ก่อนทดลอง              | ข้อมูล Bitcoin รายชั่วโมง 2013–2020 ไม่รองรับ threshold ทุกเหรียญหรือทุก timeframe               |
| Bailey et al., [Probability of Backtest Overfitting](https://www.davidhbailey.com/dhbpapers/backtest-prob.pdf), 2017    | เก็บทุกการทดลองและตรวจความเสี่ยงจากกระบวนการเลือกผู้ชนะ          | PBO ไม่ใช่โอกาสขาดทุนในอนาคต และไม่แทน chronological simulation                                  |
| Bailey/López de Prado, [Deflated Sharpe Ratio](https://www.davidhbailey.com/dhbpapers/deflated-sharpe.pdf), 2014        | ปรับการตีความ Sharpe เมื่อเลือกจากหลายการทดลองและผลตอบแทนไม่ปกติ | ต้องมีชุดผลตอบแทนและข้อมูลจำนวน/ความสัมพันธ์ของ trials; ไม่ซ่อมข้อมูลรั่วหรือ fill ที่สมมติผิด   |
| Busseti/Ryu/Boyd, [Risk-Constrained Kelly](https://web.stanford.edu/~boyd/papers/kelly.html), 2016                      | วิเคราะห์การเติบโตควบคู่ drawdown ภายใต้แบบจำลอง                 | distribution/ความน่าจะเป็นต้องน่าเชื่อถือ; ไม่ใช้ win rate จาก backtest ผู้ชนะมาคำนวณ full Kelly |

**การตัดสินใจของแผน:** เริ่มด้วยสมมติฐานจำนวนน้อยและวัดผลบน venue จริง เก็บทุกผลล้มเหลวด้วย ไม่เพิ่ม indicator, machine learning หรือ news agent เพราะดูซับซ้อนกว่า

## 4. กลยุทธ์ที่จะนำมาเปรียบเทียบ

ชื่อด้านล่างเป็น research families ยังไม่ใช่ชื่อ policy ที่เปิดเงินจริงได้ ต้องตรึงพารามิเตอร์และ version ก่อน replay

| Family            | หลักฐานที่ใช้                                                              | กรอบเวลาวิจัย                          | การออก/สิ่งที่ต้องพิสูจน์                                                                       |
| ----------------- | -------------------------------------------------------------------------- | -------------------------------------- | ----------------------------------------------------------------------------------------------- |
| Current baseline  | กฎและ hash ของ v2 ที่บันทึกอยู่                                            | native 5/15/60 นาที ตามกฎเดิม          | replay ตรงกับพฤติกรรมเดิมก่อนเปรียบเทียบ                                                        |
| Intraday trend    | relative strength, EMA/structure, volume, spread/depth, ATR                | 5/15/60 นาที; candidate ถือเป็นชั่วโมง | เทียบ breakout/pullback ที่มีอยู่ ผลหลังต้นทุนและ false breakout                                |
| Intraday reversal | range/volatility และโครงสร้างฟื้นตัวที่สังเกตได้                           | 5/15/60 นาที                           | ไม่ซื้อเพียง RSI ต่ำหรือราคาลงมาก; ทดสอบแยกจาก trend                                            |
| Swing             | completed 4h/1D, relative strength, โครงสร้างแนวโน้ม และ thesis ของโครงการ | candidate 2–60 วัน                     | มี thesis invalidation, risk budget และ review cadence ของตัวเอง; ไม่ติด time exit ของ intraday |
| News overlay      | ข่าวที่ผูก asset และเวลารับรู้ได้                                          | ใช้กับ family ที่กำหนดไว้              | เปรียบเทียบเปิด/ปิด overlay; ไม่เพิ่ม sleeve ข่าวอีกชุดก่อนพิสูจน์ประโยชน์                      |

- ข่าวใช้ประกอบการวิจัย/จัดอันดับสมมติฐานก่อน การใช้เป็น input ของ execution ภายหลังต้องมี deterministic contract และ policy รุ่นใหม่ที่ผ่านการทบทวน
- ช่วงแรกใช้ **หนึ่งเจ้าของแผนต่อ symbol** ใน portfolio replay ห้ามเปิด intraday และ swing ทับ inventory เดียวกัน หากจะถือพร้อมกันต้องมี sleeve/lot/order ownership และ reconciliation ก่อน
- เปรียบเทียบ intraday-only กับ mix intraday/swing บนทุนเดียวกัน ไม่บวกผลตอบแทนของ backtest แยกเหรียญเข้าด้วยกัน
- ล็อกชุดทดลองเริ่มต้นไม่เกิน 12 configurations รวม timeframe, exit, allocation และ overlay ที่ใช้เลือกผู้ชนะ ก่อนเห็น holdout การเพิ่ม configuration ต้องบันทึกเป็น trial ใหม่
- Indicators ที่ใช้วินิจฉัยไม่กลายเป็นเงื่อนไข execution โดยอัตโนมัติ บันทึกสูตร warm-up การปรับข้อมูล และเวอร์ชัน

### กรณีศึกษา KUB และการถือสองเดือน

ข้อมูลสาธารณะที่อ่านวันที่ 19 ตุลาคม 2026 เวลา **23:44 Bangkok**: ticker ล่าสุดประมาณ ฿18.25; close ของ daily UTC ที่จบแล้ววันที่ 18 ตุลาคม ฿18.30, SMA20 ฿19.816, SMA50 ฿20.115 และ low/high ของ 60 daily bars ล่าสุด ฿17.85/฿22.90 คำนวณจาก [Bitkub ticker](https://api.bitkub.com/api/v3/market/ticker?sym=KUB_THB) และ [native history](https://api.bitkub.com/docs/endpoint/tradingview/history?method=GET) โดยตัด daily bar ที่ยังไม่จบออก ตัวเลขนี้เป็น snapshot เพื่ออธิบายวิธีวิจัย ไม่ใช่ราคาเข้าออเดอร์

ราคาต่ำกว่าค่าเฉลี่ยแสดงลักษณะเส้นทางราคา ไม่ได้พิสูจน์ว่าต่ำกว่ามูลค่าหรือเป็นแนวรับที่ยืนได้ [Whitepaper V5.1](https://www.kubchain.com/docs/EN_KUB_Chain_WhitePaper_V5.1.pdf) อธิบาย utility และการเผาค่าธรรมเนียม staking รายไตรมาส ส่วน automated on-chain burn ยังเขียนเป็นการพัฒนาในอนาคต KUB ไม่ให้สิทธิ์เป็นเจ้าของ Bitkub จึงไม่แปลงกำไรบริษัทเป็นกระแสเงินสดของผู้ถือเหรียญ

| คำถามสำหรับ thesis 60 วัน | หลักฐานที่ต้องเก็บ                                                                                                 |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| อะไรจะทำให้อุปสงค์เพิ่ม   | usage/fees ที่ตรวจสอบได้ การใช้งานที่ไม่ใช่ธุรกรรมตนเอง และวันเปิดใช้งานจริง                                       |
| อุปทานจะเปลี่ยนอย่างไร    | burn transaction ที่ยืนยันแล้ว unlock/treasury/staking flows และผู้ควบคุม supply; ไม่ใช้แผนเก่าแทน supply ปัจจุบัน |
| เหตุการณ์จะเกิดเมื่อไร    | ประกาศต้นฉบับ วันที่มีผล เทียบกับ firstSeenAt และสิ่งที่เกิดไปแล้ว                                                 |
| ซื้อแล้วขายออกได้หรือไม่  | depth/spread/volume หลายช่วงเวลาและขนาดที่จะใช้ ไม่ใช่เฉพาะ last price                                             |
| สมมติฐานผิดเมื่อไร        | เหตุการณ์ไม่เกิด การใช้เครือข่ายไม่ดีขึ้น โครงสร้างราคาพัง หรือ risk budget ไม่พอ                                  |

ยังไม่ได้ยืนยัน catalyst ใหม่ที่ทำให้โอกาสกำไร 60 วันสูงกว่าโอกาสขาดทุน การตรวจ [รายงานโครงการปี 2025](https://medium.com/@KUBChain/kub-annual-report-2025-b96ad4c2aa68) และ [ประกาศเปลี่ยนผู้ให้บริการ staking](https://staking.kubchain.com/User_Annoumcement-20260128.pdf) ต้องแยก roadmap และเหตุการณ์ที่มีผลไปแล้วออกจากข่าวใหม่ ผลการวิจัยควรออกเป็น thesis ที่ตรวจซ้ำได้ ไม่ใช่ Buy เพราะราคาต่ำ

## 5. งบลงทุน: Aggressive แต่คำนวณทั้งพอร์ต

### 5.1 เงินทุนและความเสี่ยงคนละค่า

| Budget              | วิธีคิด                                                                                         |
| ------------------- | ----------------------------------------------------------------------------------------------- |
| Verified equity     | เงินสดรวม inventory ณ valuation ที่ตรวจสอบได้; เงินจองไม่บวกซ้ำ                                 |
| Spendable cash      | ใช้ capital reader เดิมที่หัก reservations แล้ว; ไม่หักซ้ำและไม่ใช้เงินที่จะได้จาก pending sell |
| Exposure            | รวมเหรียญที่ถือและส่วน buy ที่อาจ fill จาก orders/proposals/intents ตาม semantics เดิม          |
| Per-trade risk      | downside ถึง stop scenario รวมค่าเข้า/ออกและ slippage ไม่ใช่ notional ของดีล                    |
| Portfolio risk      | หัก downside ของ positions และ pending entries ทั้งหมดจาก risk budget รวม                       |
| Correlated risk     | วัดการตกพร้อมกัน ใช้กลุ่ม crypto ร่วมแบบระมัดระวังเมื่อ covariance ยังไม่น่าเชื่อถือ            |
| Operational reserve | ค่าธรรมเนียม rounding และภาระที่ตรวจสอบได้; ไม่กำหนด cash floor สูงเพียงเพื่อให้พอร์ตดูปลอดภัย  |

Risk-to-stop ของสถานะเดิมวัดจาก mark ปัจจุบันถึง stop พร้อมต้นทุนออก แยกจาก original entry risk เพื่อไม่ปนกำไรที่ยังไม่ขาย; stop ที่ไม่มี/ใช้ไม่ได้เป็น Unknown ไม่เป็นศูนย์ วัด gap/liquidity stress อีกชุด เพราะ stop โดยเฉพาะ LIMIT ไม่รับประกันราคาขายหรือขาดทุนสูงสุด

### 5.2 สูตร sizing ที่ต้องทดสอบ

กำหนด `E` เป็น verified equity, `d` เป็น downside fraction ของ entry ถึง stop/ATR scenario รวมต้นทุนที่ไม่ซ้ำกัน และ `Rused` เป็นความเสี่ยงของสถานะ/คำสั่งที่จองอยู่แล้ว:

```text
remainingPortfolioRisk = max(0, E * portfolioRiskLimit - Rused)
tradeRiskCapacity      = E * tradeRiskLimit / d
portfolioRiskCapacity  = remainingPortfolioRisk / d
candidateNotional      = min(cashCapacity, exposureCapacity,
                             concentrationCapacity, tradeRiskCapacity,
                             portfolioRiskCapacity, liquidityCapacity)
```

- `d` ต้องเป็นบวกและมี provenance; ปัดจำนวนตามกฎตลาด แล้วตรวจทุน/ความเสี่ยงซ้ำหลัง rounding
- สูตรหารด้วย `d` ใช้ได้เมื่อเป็นค่าคงที่หรือ conservative bound ของช่วงขนาดนั้น หาก slippage/impact เปลี่ยนตามขนาด ต้องตรวจ `d(notional)` กับ depth/participation อีกครั้ง และลดขนาดแบบ bounded จนผ่านทุก cap; observed-book replay ยังไม่พิสูจน์ market impact จริง
- `candidateNotional` เป็นเพดาน ไม่ใช่ยอดที่ต้องซื้อเต็มเสมอ ตรึง desired allocation, ลำดับจัดสรร, tie-break และจังหวะ reserve สำหรับสัญญาณพร้อมกันก่อนทดสอบ ใช้ข้อมูล cutoff เดียวกันและ commit reservations แบบ atomic ไม่ให้ผลขึ้นกับลำดับวนอ่านไฟล์/รายชื่อเหรียญ
- Reservations ต้องรวมส่วนที่ยังไม่ fill ส่วนที่ fill แล้วเป็น inventory; ห้ามนับทั้งสองฝั่งซ้ำ หรือปล่อยงบก่อนยืนยัน cancel
- ตัวอย่างเพื่ออธิบายสูตร: equity 100 หน่วย งบเสี่ยงดีล 2 หน่วย และ downside รวม 6% จะรองรับ notional 33.33 หน่วยก่อนติด cap อื่น ตัวเลขนี้ไม่ใช่ข้อเสนอขนาดออเดอร์เงินจริง
- ใช้ expected payoff หลังต้นทุนเพื่อจัดอันดับเมื่อมีการประเมินที่สอบเทียบได้ หากยังไม่มี ให้แสดงเป็น hypothesis score ไม่สร้าง win probability หรือ expected profit ปลอม
- การเพิ่มทุนไม่ทำให้เกิด alpha; ไม่มีการเพิ่มขนาดเพื่อไล่คืนขาดทุนหรือไล่ตามเป้าใกล้ deadline

### 5.3 ชุดความเสี่ยงสำหรับทดลอง

ตัวเลขต่อไปนี้เป็น **candidate ใน offline/shadow เท่านั้น** เลือกเพื่อเปรียบเทียบผลของ sizing ยังไม่มีหลักฐานว่าดีที่สุด ไม่เปลี่ยน mandate ที่บันทึกอยู่ และต้องนับรวมใน trial budget:

| Candidate    | Exposure สูงสุด   | Concentration ต่อเหรียญ | Stop scenario ต่อดีล / รวมพอร์ต |
| ------------ | ----------------- | ----------------------- | ------------------------------- |
| Control      | ตามกฎรุ่นปัจจุบัน | ตามกฎรุ่นปัจจุบัน       | ตามกฎรุ่นปัจจุบัน               |
| Aggressive A | 100%              | 60%                     | 2% / 8% equity                  |
| Aggressive B | 100%              | 100%                    | 4% / 12% equity                 |

คง daily loss/drawdown constraints เป็นข้อจำกัดอีกชั้นตาม protocol ที่ตรึงไว้ ไม่ reset ความเสี่ยงของสถานะค้างเมื่อข้ามวัน Daily-loss pause หยุด entries ใหม่ตามกฎ แต่ไม่ทำให้การดูแล exits/reconciliation หยุดตามไปด้วย สถานะ Unknown, ข่าวขัดแย้ง หรือข้อมูลขาดเป็นข้อจำกัดเฉพาะที่เกี่ยวข้อง ไม่ใช่เหตุให้เลิกวิจัยทั้งตลาด

ประเมินทั้งผลสุทธิ drawdown การลดลงพร้อมกัน time underwater turnover ความจุ execution และผลหากออกไม่ได้ ตัดสินจากการแลกผลตอบแทนกับความเสี่ยงที่เห็นจริง ไม่เรียก B ว่าดีกว่าเพราะใช้เงินมากกว่า ไม่มี leveraged candidate ในชุดนี้

### 5.4 Opportunity cost และเป้าหมาย

- เปรียบเทียบ cash, costed passive benchmark และ candidate ที่ซื้อได้จริง ณ เวลาเดียวกัน ด้วยทุนและข้อจำกัดเดียวกัน
- เงินที่ติด orders/positions ไม่สามารถใช้ซื้อทุกโอกาสย้อนหลังได้พร้อมกัน เก็บ rejected candidates และ binding constraint
- แยกค่าเสียโอกาสเชิงสมมติออกจากกำไรบัญชีและ alpha ที่พิสูจน์ได้ ไม่สรุปจากกราฟที่ขึ้นดีที่สุดหลังจบวัน
- รายงาน equity และเงินเติมทุนสำหรับเป้าหมายธุรกิจ; ประเมินกลยุทธ์ด้วย net trading P&L และ flow-adjusted return เพิ่มเติม ห้ามให้เงินเติมทุนกลบผลขาดทุน

## 6. เครื่องมือ ข้อมูล และงบค่าใช้จ่าย

ตรวจข้อมูลแพ็กเกจวันที่ 19 ตุลาคม 2026 ราคา USD ยังไม่รวมภาษี/FX และต้องตรวจอีกครั้งก่อนสมัคร **ไม่มีการซื้อหรือติดตั้งเพิ่มในงานวางแผนนี้**

| ตัวเลือก                             | เหมาะกับอะไร                                      | ราคา/ข้อจำกัดที่ตรวจได้                                                                             | ข้อสรุป                                                                                                                                                                                   |
| ------------------------------------ | ------------------------------------------------- | --------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Bitkub public REST/WS + native OHLCV | ราคาของ venue, depth ปัจจุบัน, completed candles  | public access; ไม่พบ guaranteed historical book archive/retention/SLA                               | ใช้เป็นฐานหลักและวัด coverage เอง ([docs](https://github.com/bitkub/bitkub-official-api-docs), [history](https://api.bitkub.com/docs/endpoint/tradingview/history?method=GET))            |
| ประกาศโครงการ/โบรกเกอร์              | maintenance, delisting, security, supply changes  | ไม่ยืนยัน feed API/สิทธิ์เผยแพร่ซ้ำของทุกเว็บไซต์                                                   | adapter เฉพาะแหล่งที่ตรวจสิทธิ์และตัวอย่าง payload แล้ว ([Bitkub](https://support.bitkub.com/en/solutions?Id=151000093916))                                                               |
| GDELT DOC                            | ค้น metadata/URL ข่าวนอก watchlist                | dataset fee-free; docs ระบุ window 3 เดือนและสูงสุด 250 บทความ/คำขอ ไม่มี latency SLA ที่ยืนยัน     | prototype metadata + dedup; สิทธิ์ข้อความของสำนักข่าวแยกต่างหาก ([API](https://blog.gdeltproject.org/gdelt-doc-2-0-api-debuts/), [terms](https://gdeltproject.org/about.html#termsofuse)) |
| CoinGecko                            | cross-market context/market cap                   | Demo $0, 10k calls/เดือน, 100/นาที; Basic $35/เดือน, Analyst $129/เดือน; annual price ต่างกัน       | Demo สำหรับ prototype ตาม terms; ไม่แทน Bitkub intraday history ([pricing](https://www.coingecko.com/en/api/pricing))                                                                     |
| CoinGecko historical range           | history ตามความละเอียดของ endpoint                | explicit 5m เป็น Enterprise-only สูงสุด 10 วัน/คำขอ                                                 | อย่าซื้อ Analyst เพราะเข้าใจว่าได้ deep 5m history ([granularity](https://docs.coingecko.com/reference/coins-id-market-chart-range))                                                      |
| KUB RPC / explorer                   | ตรวจ network/contract และ transactions ที่ระบุได้ | มี public connection; archive/quota/labels/exchange-flow series ยังไม่ยืนยัน                        | เริ่ม read-only เฉพาะตัวชี้วัดที่ตรวจซ้ำได้ ([connection](https://docs.kubchain.com/about-kub/connect-to-kub))                                                                            |
| CryptoPanic                          | ตัวเลือก aggregator ภายหลัง                       | official page อ่านรายละเอียดแพ็กเกจไม่ได้ จึงยังยืนยันราคา/history/สิทธิ์ไม่ได้                     | ไม่รวมในงบฐาน ไม่ใช้ราคาในบทความเก่า ([official](https://cryptopanic.com/developers/api/))                                                                                                |
| DefiLlama                            | DeFi context ที่เกี่ยวข้องกับ thesis              | Pro UI ไม่รวม API; หน้า subscription แสดง annual-equivalent $250/เดือน แต่ API docs ระบุ $300/เดือน | optional; ตรวจ billing และ KUB coverage ก่อน ไม่สมัครเพื่อเพิ่มจำนวนเครื่องมือ ([plans](https://defillama.com/subscription), [API](https://api-docs.defillama.com/))                      |

Bitkub ระบุ native resolution 1/5/15/60/240/1D แต่ความสมบูรณ์ย้อนหลังต้องตรวจรายคู่ ห้ามเติมช่องว่างเป็น zero volume โดยไม่มี semantics รองรับ Public stream `market.trade.<symbol>` ถูกประกาศปิด 18 พฤษภาคม 2026; ใช้ ticker/orderbook ที่ยัง documented และตรวจ continuity ของ `tradeschanged` ก่อนอ้างว่าได้ trade tape ครบ ([WebSocket documentation](https://github.com/bitkub/bitkub-official-api-docs/blob/master/websocket-public.md))

[อัตราค่าธรรมเนียม Bitkub THB](https://www.bitkub.com/en/fee) ปัจจุบันระบุ 0.25% ต่อฝั่ง ทั้ง maker/taker ดังนั้น round trip ประมาณ 0.5% ก่อน spread/slippage เป็น baseline ไม่ใช่ breakeven ที่รับประกัน แยก fee cash ที่จ่ายจริง fee credits ที่ตรวจสอบแล้ว และกรณี credits หมดใน stress test

### เครื่องมือทดสอบกลยุทธ์

| ตัวเลือก              | เหตุผลที่พิจารณา                                               | การตัดสินใจเริ่มต้น                                                                                                                                                                                                                                                                                                                                                                                   |
| --------------------- | -------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Existing C# evaluator | ใช้ decision logic และ domain/accounting เดิมได้               | ขยายแบบ offline ให้ replay portfolio ได้ ไม่สร้าง framework ทั่วไปและไม่อ้างอิง broker writer                                                                                                                                                                                                                                                                                                         |
| vectorbt              | screening แบบ array ได้เร็ว                                    | ไม่จำเป็นในรุ่นแรก; terms เป็น Apache 2.0 + Commons Clause และ optional dependencies อาจต่างกัน ([terms](https://vectorbt.dev/terms/))                                                                                                                                                                                                                                                                |
| backtesting.py        | prototype แบบ bar-by-bar และ independent accounting comparison | single OHLCV stream ไม่แก้ shared-capital โดยตรง; AGPL-3.0 ต้องตรวจเงื่อนไขก่อนรวมผลิตภัณฑ์ ([API](https://kernc.github.io/backtesting.py/doc/backtesting/backtesting.html), [license](https://github.com/kernc/backtesting.py/blob/master/LICENSE.md))                                                                                                                                               |
| LEAN                  | event-driven multi-asset C#/Python                             | engine Apache-2.0 แต่ CLI local backtests ต้อง paid organization; built-in fills สมมติ complete fill ต้องปรับ partial fill เอง ไม่ย้ายเพียงเพราะฟีเจอร์เยอะ ([engine](https://github.com/QuantConnect/Lean), [CLI](https://www.quantconnect.com/docs/v2/lean-cli/backtesting/deployment), [fills](https://www.quantconnect.com/docs/v2/writing-algorithms/reality-modeling/trade-fills/key-concepts)) |

**งบเริ่มต้น:** incremental paid data = $0 จนกว่าจะมี coverage audit และประโยชน์ที่ต้องซื้อชัดเจน ค่าเครื่อง/ไฟ/จัดเก็บและ AI subscription เดิมยังเป็นต้นทุนจริง แสดง operating cost แยกจาก broker net และ portfolio equity บริการเพิ่มต้องพิสูจน์มูลค่าเพิ่มบน holdout หลังหักค่าใช้จ่าย ไม่หักค่า subscription เดิมซ้ำ

## 7. ข่าว: รับรู้ทันและตรวจย้อนกลับได้

ต่อยอด [news plan เดิม](news-aware-trading-plan.md) และ revision/as-of store ไม่สร้าง news database อีก service

1. Ingest เฉพาะ source allowlist ตรวจ project/chain/contract identity แยก ticker ที่ชื่อซ้ำ; เก็บต้นฉบับเท่าที่สิทธิ์อนุญาต
2. บันทึก event/effective/published/firstSeen/fetched/updated time, revision, source ID, canonical URL, parser version และ coverage/outage
3. Deduplicate ข่าวต้นทางเดียวกัน การแปล และการแก้ข่าว; หลายเว็บคัดข่าวเดียวไม่ใช่ independent corroboration
4. News specialist ระบุ fact/first-party claim/rumour/correction แยกสิ่งที่ยืนยันแล้วจาก roadmap และข่าวเก่าที่เพิ่งดึงพบ
5. สร้างคำอธิบายผลต่อ demand/supply/tradability, horizon, สิ่งที่ราคาอาจสะท้อนแล้ว และหลักฐานหักล้าง; ไม่มี sentiment score เป็นคำสั่งซื้อขายโดยตรง
6. ส่ง evidence IDs กับ as-of packet ให้ specialist ที่เกี่ยวข้อง ไม่ส่งบทความเต็มทุกชิ้นให้ CEO

ข่าวและเอกสารภายนอกเป็น **ข้อมูลที่ไม่น่าเชื่อถือสำหรับคำสั่งระบบ** ห้ามทำตามข้อความที่ขอให้รันคำสั่ง เปิดเผย credentials เปลี่ยนสิทธิ์ หรือส่งออเดอร์ เครื่องมือ fetch อยู่ฝั่ง server และสิทธิ์ worker เป็น read-only เท่าที่จำเป็น การไม่มีข่าวไม่เท่ากับข่าวดี และ source outage ไม่ใช่ market-wide sell signal

ก่อนใช้ news feature ใน backtest ต้องอ่าน revision ที่รู้ได้ ณ เวลานั้น ข้อมูลที่เพิ่ง backfill ไม่มี first-seen ย้อนหลังที่เชื่อถือได้ให้ใช้ prospective เท่านั้น

**Historical LLM leakage:** แม้ packet จะเป็น as-of โมเดลปัจจุบันอาจรู้เหตุการณ์หลังวันนั้นจากการฝึกอยู่แล้ว จึงไม่ใช้ thesis/score ที่เพิ่งให้ LLM วิเคราะห์อดีตมาอ้างเป็น untouched out-of-sample evidence ใช้ได้เฉพาะผลที่เก็บ contemporaneously และยืนยันเวลาจริงได้, deterministic transformations ของ as-of data ที่ตรวจสูตรได้ หรือ prospective evaluation ของรุ่นที่ตรึงแล้ว ความสามารถเล่าอดีตได้ไม่เป็น alpha

## 8. ทีม AI: แยกหน้าที่และ skill เท่าที่ใช้

เป็น logical roles ใน Team/ai-worker เดิม ไม่สร้างหนึ่ง microservice หรือหนึ่ง model process ต่อคน ทุกคนมี `roles/<role>/AGENTS.md` และ `SKILL.md` ของตัวเอง โหลดเฉพาะคู่ไฟล์ของผู้รับงานกับ shared authority contract สั้น ๆ

| Role              | งานและ trigger                                           | Skill/ข้อมูลที่จำเป็น                                              | ผลลัพธ์ที่ต้องส่ง                                                          |
| ----------------- | -------------------------------------------------------- | ------------------------------------------------------------------ | -------------------------------------------------------------------------- |
| CEO               | จัดลำดับงานค้าง/ข้อขัดแย้ง สรุปตามรอบและเหตุสำคัญ        | research routing, อ่าน summaries/ข้อจำกัดงบ ไม่อ่าน raw ทุกชุด     | priorities, task IDs, ผู้รับผิดชอบ, next review; ไม่มีสิทธิ์จัดสรรเงินจริง |
| Intraday Analyst  | completed-bar candidate ใหม่หรือ setup เปลี่ยน           | trend/range, liquidity และ round-trip costs ของคู่ที่ได้รับ        | setup/horizon/invalidation พร้อม evidence IDs                              |
| Swing Analyst     | daily close, thesis เปลี่ยน หรือมี candidate ระยะหลายวัน | project economics, supply/events, 4h/1D structure                  | frozen thesis, review date และเหตุออกของ swing                             |
| News Verifier     | ข่าวใหม่/แก้ไขที่เกี่ยวข้อง                              | source/identity/time verification; เฉพาะ metadata/เนื้อหาที่อนุญาต | fact status, affected assets, freshness, contradiction                     |
| Position Reviewer | การถือจริงมี material change หรือถึง review deadline     | entry thesis, fills, exit plan และข้อมูลปัจจุบัน                   | Hold/Review/Invalidated เชิงวิจัย ไม่ส่งคำสั่งขาย                          |
| Quant Researcher  | งานเปรียบเทียบที่มี dataset พร้อม; daily research queue  | อ่านผล offline replay, trial registry และ diagnostics              | comparison, uncertainty, failure analysis; ไม่แต่งตัวเลขเมื่อยังไม่ได้รัน  |
| Evidence Reviewer | ก่อนยอมรับ assessment/thesis หรือพบข้อขัดแย้ง            | lineage, as-of, costs, schema และขอบเขต authority                  | Accept/Reject/Insufficient evidence สำหรับรายงาน ไม่ใช่อนุมัติเงินจริง     |

- Collector, indicator calculations, budget allocator และ execution/reconciliation เป็น deterministic jobs/classes ไม่ใช้ LLM แทนเลขคณิตหรือการส่งคำสั่ง
- Dispatch เป็น persisted task graph แต่ละ task มี account/run/event/version, input evidence IDs, owner, deadline, idempotency key และ output schema
- แต่ละ attempt/output ผูก account, policy hash, immutable evidence revision/cutoff, input digest และ expiry ตรวจซ้ำก่อนรวมเป็น assessment; ผลจาก account/policy เก่าหรือข้อมูลที่หมดอายุไม่ถูกนำมาผสม ผลหลาย cutoff ใช้ร่วมกันได้เฉพาะ compatibility/freshness rules ที่ตรึงและตรวจได้
- Recovery แยก claimed-but-not-started, started, completed และ uncertain; บันทึก attempt ก่อน side effect ไม่เริ่ม model call ที่ผลไม่แน่นอนซ้ำอัตโนมัติ และไม่คืน budget จนพิสูจน์ได้ว่าไม่ได้ใช้ เก็บผล late/stale เพื่อ audit แต่ไม่เปิด dependent Reviewer/CEO จากผลนั้น
- งาน independent และ task ใหม่ที่มีหลักฐานเปลี่ยนยังเดินต่อได้ตาม budget/cooldown; attempt ที่ Unknown ไม่กลายเป็นการหยุดทั้งบริษัทหรือการสร้างงานเดิมซ้ำเพื่อเลี่ยง dedup
- บทบาทเดิม migrate แบบ additive รักษา enum/รายงานเก่า ไม่เปลี่ยนชื่อ role จนอ่านประวัติไม่ได้
- งานหนึ่งใช้ specialist เดียวก่อน เรียก Reviewer เมื่อผลนั้นจะถูกนำไปรวม assessment และ CEO เฉพาะการตัดสินข้อขัดแย้ง/สรุป ไม่ต้องประชุมเจ็ดคนทุกเหตุการณ์
- เริ่ม concurrency หนึ่ง LLM task ต่อบัญชี มี timeout และ cancellation; เพิ่มเมื่อมี backlog/latency ที่วัดได้และ provider capacity รองรับ
- Worker ไม่ได้รับ credentials, control token, broker writer, shell หรือเครื่องมือเปลี่ยนบัญชี Dataset preparation/replay เป็นงาน server ที่มีขอบเขตชัดเจน
- เก็บ task สถานะ Done/Pending/Blocked พร้อมเหตุและเงื่อนไขเปิดใหม่ งานขาดข้อมูลไม่ถูกสั่งซ้ำทุกชั่วโมง และไม่บล็อกงานอิสระ

### Context และ budget ของ AI

| ชั้น       | กติกาที่เสนอ                                                                                                                                |
| ---------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| Input      | task packet เดียว บทบาทเดียว รายการหลักฐานจำกัดและโหลดเพิ่มตาม ID ผ่าน server; ไม่ส่ง full backlog/chat                                     |
| Output     | structured fields ก่อน summary สั้น; ไม่ใช้ narrative ยาวเป็น input ต่อกันทุก role                                                          |
| Usage      | เก็บ actual input/output/cached usage เมื่อ provider ส่งให้; unavailable เป็น Unknown ไม่แปลงจำนวนอักษรเป็น measured tokens                 |
| Cost       | Codex login quota และเงินสด subscription เป็นคนละตัวชี้วัด ไม่ตีราคา API token ให้ subscription โดยเดา                                      |
| Admission  | reserve budget ก่อนเริ่มงาน คืนส่วนที่ยืนยันว่าไม่ใช้ และเก็บผล uncertain usage ไม่เป็นศูนย์                                                |
| Priority   | แยก budget สำหรับ held-position/material events กับ discretionary exploration กระจาย allowance ตลอด rolling 24 ชั่วโมง ไม่ใช้หมดก่อนกลางคืน |
| Exhaustion | พักเฉพาะ LLM tasks ที่เกินงบ บอกสาเหตุ/next eligible time; collectors, deterministic exits และ reconciliation ทำต่อ                         |

R0 ต้องวัด usage/latency จริงก่อนตรึง per-role limits และสัดส่วน reserve ห้ามเพิ่มจำนวน agents แล้วคูณโควตาทั้งทีมตามจำนวนคน หาก provider quota หมด การแสดงสถานะต้องตรงจริง ไม่สัญญาว่าจะมี AI message ทุกชั่วโมง

Fallback ที่ implement สำหรับ R4: เก็บ actual usage จาก SDK เมื่อมี มิฉะนั้นเป็น Unknown; เริ่มงานได้สูงสุด 2 ordinary reservations และ 1 held-position reservation ต่อ rolling hour รวมไม่เกิน 3 ไม่มีเพดาน 24/32 รอบต่อวันตามคำขอให้ทำงานต่อเนื่อง มี durable reservation และ request-size/output/time ceilings ไม่ refund งาน failed/uncertain โดยเดา Preserve claims เดิมขณะ migration; restart/midnight ไม่ลบประวัติ rolling hour เพดานนี้จำกัดจำนวนงาน ไม่ใช่ token/monetary spending cap และไม่ข้าม quota ของ provider ดู [พฤติกรรมและข้อจำกัดที่ implement](continuous-research.md)

## 9. ทำงาน 24 ชั่วโมงโดยไม่รอประชุม

| Loop                | งาน                                                                | Cadence/เงื่อนไขเป้าหมาย                                                                                    |
| ------------------- | ------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------- |
| Market/execution    | feed, freshness, deterministic signal/exits, broker reconciliation | ทำต่อเนื่องตาม cadence ของระบบเดิม ภายใน mandate ที่ยังใช้ได้                                               |
| Research dispatcher | ตรวจ task/event/deadline/source health                             | 1 นาที; dedup และ cooldown ต่อ event/task ไม่ใช้ตัวเดียวขวางงานทุกประเภท                                    |
| Intraday/positions  | candidate ใหม่/ข้อมูลสำคัญเปลี่ยน/ถึงกำหนด                         | event-driven; hourly fallback ของรายการที่ยังไม่มี assessment สด เมื่อ budget/provider พร้อม                |
| News                | ตรวจแหล่งและส่งเฉพาะ material updates                              | ตาม source quota และ cache; วัด firstSeen-to-assessment lag ไม่มี SLA ที่อ้างจากผู้ให้บริการโดยไม่มีหลักฐาน |
| Swing               | thesis/catalyst และ completed daily data                           | daily close และ material events ระหว่างวัน ไม่ออกเพียงเพราะจบรอบประชุม                                      |
| Quant               | ประเมินชุดทดลองที่ตรึงและงานตรวจคุณภาพ                             | คิวรายวัน/เมื่อ dataset เปลี่ยน ไม่ rerun ผลเดิมซ้ำ                                                         |
| CEO                 | priorities และข้อขัดแย้ง                                           | summary ตามกำหนดและ exceptions; ไม่เป็นคอขวดของทุก market event                                             |

แก้สถานะพักให้บันทึกเหตุผลลง storage และแสดง `last completed`, `next eligible`, budget/provider status และงานที่ยังทำอยู่ Restart ต้องไม่ล้าง quota หรือทำงานที่ไม่แน่นอนซ้ำ Midnight เป็นเพียง reporting boundary ไม่ใช่เวลาปิดบริษัท ข้ามคืนยังคง position risk, pending orders และงานที่ถึงกำหนด

## 10. วิธีพิสูจน์กลยุทธ์

### Data และ experiment freeze

- Audit universe/history ก่อนดูผลกำไร เป้าความครอบคลุมเริ่มสำรวจ 6–12 เดือนสำหรับ intraday และ 24–36 เดือนสำหรับ swing แต่ช่วงเวลานี้ไม่รับประกันความเพียงพอหรือว่า provider มีข้อมูลครบ
- เพิ่ม research-only 4h/1D และ long-window dataset contract แยกจาก `TradingTimeframes` ของ execution เดิม MarketData fetch แบบ chunk ไม่เกินขอบเขต request ที่ตรวจสอบแล้ว มี pagination/dedup/gap checks, manifest/source/hash และ UTC close semantics ของ daily bars; ห้ามเพิ่ม timeframe ลง v1/v2/v3 เพื่อให้ study อ่านได้
- เก็บ as-of listings/delistings, trading status, tick/minimum size, timestamp semantics และ missing bars ไม่เลือกเฉพาะเหรียญที่ยังอยู่หรือผู้ชนะวันนี้
- Freeze data snapshot/hash, calendar folds, candidate configurations, fee/fill/latency assumptions, benchmarks, stress cases และ acceptance thresholds ก่อนอ่าน final holdout
- แบ่งตามเวลาทั้ง universe ใช้ nested chronological walk-forward เลือกจากอดีตเท่านั้น และเก็บ final holdout ที่ใช้ตัดสินเพียงครั้งเดียว
- Purge labels ที่ช่วงผลลัพธ์ทับ test interval โดยใช้ actual label-end time โดยเฉพาะ swing 60 วัน; embargo buffer เพิ่มเติมต้องตรึงพร้อมเหตุผล
- Warm-up อ่านได้เฉพาะอดีตจริง ไม่มี future candle, revised news หรือ scaler/eligibility ที่ fit จาก test; boundary holdings ต้อง carry หรือปิดตาม protocol พร้อมต้นทุนจริง ไม่ reset ทุนฟรี
- ยืนยัน historical LLM outputs ตามกติกา leakage ในข้อ 7 และแยก historical vendor backfill ออกจากข้อมูลที่เคย capture ณ เวลาจริง หากไม่มี revision/availability history ให้รายงานข้อจำกัด ไม่อ้างว่าเป็น snapshot ดั้งเดิม

### Portfolio และ execution replay

- ใช้ก้อนเงินเดียวทุก candidate จำลอง reservations, fills, partial fills, expiry, cancel delay, stale evidence และความเสี่ยงรวม ไม่รวมผลแยกเหรียญที่ใช้เงินซ้ำ
- คำสั่งเริ่มมีสิทธิ์ fill หลัง decision + simulated latency เท่านั้น Candle touch ไม่เป็น fill; หาก order/stop/target เกิดใน bar เดียว ใช้ข้อมูลละเอียดกว่าหรือ adverse-order sensitivity
- Book replay ไม่สามารถสร้าง market impact ของเราได้ และ queue position ยังเป็นแบบจำลองเมื่อมีเพียง market-by-price ([HftBacktest documentation](https://hftbacktest.readthedocs.io/en/latest/order_fill.html))
- เมื่อมีเพียง OHLCV ให้เรียกผลเป็น bar scenario มี pessimistic fill/no-fill cases และแยกจาก observed-book prospective evidence ห้ามแสดงเหมือน broker fills
- รวม delisting/gap, ต้นทุนสูงขึ้น, latency, feed/news outage, fee credits หมด, correlation เข้าใกล้หนึ่ง, unfilled exits และ residual inventory; missing outcome ต้องมี Unknown และ bounds ไม่โยนทิ้ง
- เงินเติมทุนใช้ schedule เดียวกันทุก benchmark หรือใช้ normalized no-flow comparison; unknown full-account basis ไม่ถูกเติมด้วยราคา spot ปัจจุบัน

### เกณฑ์ Accept / Reject / Insufficient evidence

| Gate           | สิ่งที่ต้องพิสูจน์                                                                                                                                                |
| -------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Correctness    | replay ทำซ้ำได้ cash/inventory/fee identities ตรง independent hand-calculated fixtures ไม่มี lookahead/ทุนซ้ำ                                                     |
| Coverage       | รายงานช่วงเวลา ตลาดที่ครอบคลุม gaps และเหตุ exclude; แหล่งข้อมูลของ feature ใช้ได้ ณ decision time                                                                |
| Selection      | มีทุก trial รวมที่เลิกทำ ใช้ holdout ครั้งเดียว และตรวจผลของ multiple testing; DSR/PBO ใช้เมื่อข้อมูลรองรับพร้อม sensitivity ไม่สร้างค่าทดแทน                     |
| Economic value | net หลังต้นทุนเป็นบวกและดีกว่า primary feasible benchmark ที่ตรึงไว้ พร้อม uncertainty ที่รองรับ ขนาดความได้เปรียบต้องพอจ่าย incremental operating costs          |
| Uncertainty    | ใช้ time-block/common-market-episode resampling คง dependence ระหว่างเหรียญ ไม่สุ่มทุกดีลเป็นอิสระ; กำหนดช่วงความเชื่อมั่น/วิธีแก้ trial selection ก่อนดู holdout |
| Robustness     | ไม่ได้ผลบวกจากเหรียญเดียว/ช่วงเดียวหรือ fill ที่ optimistic; stress loss/drawdown ไม่เกิน risk envelope ที่ตรึงไว้                                                |
| Prospective    | shadow ไม่ส่ง broker POST และผลสอดคล้อง execution assumptions; 72 ชั่วโมงพิสูจน์ uptime ไม่ใช่ alpha และ 20/30 ดีลไม่ใช่หลักฐานสถิติสำเร็จโดยตัวมันเอง            |
| Authority      | accepted research candidate ไม่เท่ากับ live enable; รุ่นใหม่ต้องผ่าน review/maintenance gates และเจ้าของเปิด mandate ใหม่เอง                                      |

R0 ต้องระบุ primary benchmark, minimum economically useful edge, uncertainty method/confidence level และ maximum trial count ให้เป็น protocol ก่อนทดสอบ Threshold เหล่านี้เป็นการออกแบบการทดลอง ไม่ใช่กฎสากลจาก paper หากข้อมูลไม่รองรับให้ตัดสิน **Insufficient evidence** พร้อมงานแก้ที่ทำได้ ห้ามรายงาน Accept เพราะไม่มีหลักฐานลบ

## 11. ลำดับ implement และหลักฐานจบงาน

R4 ซอฟต์แวร์และ focused scheduling implement แล้ว และติดตั้ง Team/Web หลังปิด AutoLive พร้อม canonical backups และ isolated restore ตาม [สถานะหลัก](../plan.md) ส่วน runtime pilot และ measured-token admission ยังรอหลักฐาน ดู [รายละเอียดปัจจุบัน](continuous-research.md) งาน R0–R3/R5–R7 คง Pending ตามเกณฑ์เดิม; การทำงานต่อเนื่องไม่ยืนยันประสิทธิภาพหรือกำไรของกลยุทธ์

| Phase                       | Owner                         | งาน                                                                                                                   | Acceptance / หลักฐาน                                                                                                        |
| --------------------------- | ----------------------------- | --------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| R0 — Baseline/protocol      | Lead + research jobs          | coverage/usage audit, rules/hash snapshot, trial registry, data/benchmark/risk protocol                               | protocol review ผ่านก่อนเห็น holdout; เก็บข้อมูลบัญชีในพื้นที่ส่วนตัว                                                       |
| R1 — Usable evaluation      | Portfolio + MarketData        | เลือก study จาก readiness; retain failed attempt; bounded retry; research-only 4h/1D และ long-window chunked backfill | complete/incomplete/gap/restart, UTC close และ dataset manifest tests; ไม่ค้างผล empty ทั้งวัน ไม่ขยาย execution timeframes |
| R2 — News evidence          | MarketData                    | official project/Bitkub adapters ที่อนุญาต + GDELT prototype, identities/revisions/as-of/source health                | fixtures ข่าวไทย/อังกฤษ/ชื่อซ้ำ/แก้ข่าว/เก่า/outage; coverage/latency report ไม่อ้าง completeness เกินข้อมูล                |
| R3 — Portfolio/risk replay  | Portfolio                     | shared-capital simulation, aggregate stop-risk reservations, one-plan-per-symbol ownership                            | cash/risk invariants รวม partial/cancel races; เปรียบเทียบ sizing controls ด้วย fake execution                              |
| R4 — Focused AI team        | Team + ai-worker + Web        | per-role contracts/skills, evidence-bound tasks, measured/fallback budget, fairness/deadlines/status                  | routing/Stop/Resume/account switch/policy change/stale output/restart/midnight/quota tests; ไม่มี role ได้ broker authority |
| R5 — Candidate studies      | Portfolio + Team              | intraday/swing/news-overlay frozen candidates, walk-forward + untouched holdout                                       | full trials, net/risk/capacity/uncertainty report; Accept/Reject/Insufficient evidence มีเหตุผล                             |
| R6 — Prospective review     | MarketData + Portfolio + Team | compare frozen candidates ใน read-only shadow และประเมิน execution mismatch                                           | sample/calendar/regime sufficiency ตาม protocol; ไม่ประกาศผ่านเพียงครบวัน                                                   |
| R7 — Separate live proposal | Lead                          | เฉพาะ candidate ที่ผ่าน: new policy/hash, schema/lot ownership, mandate review และ rollback design                    | review แยกก่อน implement live; เดิมไม่ถูก upgrade/activate และเจ้าของเป็นผู้เปิดเองภายหลัง                                  |

Dependency: R0 มาก่อนทุกงาน; R1/R2/R4 ทำแยกได้หลัง contracts ตรงกัน; R3 และข้อมูลพร้อมก่อน R5; R6 ต้องใช้รุ่นที่ตรึงแล้ว; R7 เริ่มเมื่อหลักฐานเพียงพอจริง ไม่รอให้ทุกเครื่องมือ optional ถูกเพิ่ม

### Verification และขอบเขตการจบ

- เขียน regression ก่อนแก้ study retry, budget starvation, risk double reservation และ sleeve ownership ใช้ fake time/provider/broker ใน CI
- ตรวจ architecture: controller บาง, CQRS, Domain ไม่มี framework, หนึ่ง type ต่อไฟล์, service HTTP boundary, lazy DI และ React class components ตามกฎเดิม
- รัน format/check/build และ deterministic/architecture suites ตามส่วนที่เปลี่ยน UI ใหม่ต้องตรวจ desktop/tablet/phone; real SQL/broker checks เป็น opt-in และไม่ส่งเงินจริง
- อัปเดต docs/role skills/operational runbook พร้อมสถานะและหลักฐานจริง ไม่ติ๊ก checked จากแผนหรือ worker summary เพียงอย่างเดียว
- การติดตั้งต้องปิด AutoLive ตรวจ canonical backups และ restore proof; deployment ไม่เปิด AutoLive ไม่ส่ง/ยกเลิก broker order เพื่อทดสอบ
- Rollback code ต้องอ่าน additive state ใหม่ได้หรือคง service รุ่นที่ดูแลสถานะนั้น; ไม่ย้อน ledger ไม่ลบทุน/ประวัติ ไม่ทิ้ง position ที่เปิดอยู่ และไม่คืน mandate โดยอัตโนมัติ
- จบแต่ละ phase ด้วยผลตรวจและข้อจำกัดที่ระบุได้ หยุดเพิ่ม dependencies/agents/indicators เมื่อ acceptance ของ phase ผ่านแล้ว; ผลวิจัย Reject เป็นผลสำเร็จของการทดลองที่ถูกต้องได้

**เกณฑ์ตัดสินว่าดีขึ้น:** โอกาสที่ใช้ได้ถูกพบและทบทวนทันเวลา งบไม่ถูกใช้ซ้ำ ความเสี่ยงรวมอธิบายได้ และผลสุทธิที่ตรวจสอบได้ดีขึ้นภายใต้ protocol ที่ตรึงไว้ จำนวนข้อความ จำนวนดีล จำนวน agents หรือกำไรสมมติที่เพิ่มขึ้นไม่ใช่ตัวแทนของเกณฑ์เหล่านี้
