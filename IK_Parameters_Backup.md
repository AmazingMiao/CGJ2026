# IK 参数备份（IK.unity 场景，改动前快照）

> 生成时间：重构为 Unity 物理 / 布娃娃之前。
> 用途：改字段名或删字段会导致 Unity 场景里已序列化的数值丢失映射，这里留存所有手调值以便恢复。
> `groundMask.m_Bits: 64` = Layer 6（即你建的 Ground 层）。

---

## TwoBoneIK2D

### 腿（左腿 IK：GameObject 440686231，origin=L_Hip；右腿 IK：GameObject 1994599398 / IK_R_Leg，origin=5723292）
- upperLength: **0.4**
- lowerLength: **0.5**
- bendLeft: **1 (true)**
- stretchToFit: **0 (false)**
- upperAngleOffset: **-90**
- lowerAngleOffset: **-90**
- jointOverlap: **0.1**

### 手臂（GameObject 509838798 与 1983663916）
- upperLength: **0.36**
- lowerLength: **0.6**
- bendLeft: **0 (false)**
- stretchToFit: **0 (false)**
- upperAngleOffset: **14.8**
- lowerAngleOffset: **-69.6**
- jointOverlap: **0.05**

---

## FootPlantIK2D（两条腿）

公共值：
- castOffset: (0, 0)
- castHeight: **1.5**
- castDistance: **1**
- groundMask: Layer 6 (Ground)
- footHeightOffset: 0
- hangWhenNoGround: 1 (true)
- enableGait: 1 (true)
- maxSwingAngle: **35**
- strideLength: **3**
- liftHeight: **0.5**
- gaitSettleSpeed: 8
- idleSpeedThreshold: 0.05

各自差异：
- **左脚**（footTarget=762148208，castOrigin=L_Hip 2025419045）：oppositePhase = **0 (false)**
- **右脚**（footTarget=392877109，castOrigin=5723292）：oppositePhase = **1 (true)**

---

## PelvisSlopeAlign2D（GameObject 承载 pelvis=1717834253）
- leftContact: 762148208（左脚 target）
- rightContact: 392877109（右脚 target）
- maxTiltAngle: **40**
- angleOffset: 0
- smoothSpeed: **12**

---

## FootSlopeAlign2D（两只脚）
公共值：
- footOffset: **(0.06, 0.07)**
- angleOffset: 0
- airborneAngle: 0
- smoothSpeed: 15

各自引用：
- **右脚**：foot=72993936，leg=IK_R_Leg(1994599400)，footPlant=392877110
- **左脚**：foot=415897232，leg=440686233(左腿 IK)，footPlant=762148209

---

## PlayerController2D（Player，GameObject 410741256）
- moveSpeed: **5**
- jumpVelocity: **10**
- coyoteTime: 0.1
- jumpBufferTime: 0.1
- shortJumpDamp: 0.5
- groundCheck: 462219209（GroundCheck，localPos y=-1.983）
- groundCheckDistance: 0.2
- groundMask: Layer 6 (Ground)

---

## BoulderPushIK2D
场景中未找到实例（尚未挂载），无参数需备份。

---

## 场景关键对象 fileID 速查
- Player: 410741256
- Pelvis: 1717834253
- L_Hip: 2025419045
- 左脚 target: 762148208 / 右脚 target: 392877109
- 左腿 IK 组件: 440686233 / 右腿 IK 组件: 1994599400
- GroundCheck: 462219209
