using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Events;

namespace IamItem
{
    /// <summary>Почему призрака выбросило. Определяет эффекты и штраф по шкале.</summary>
    internal enum StopReason : byte
    {
        Manual = 0,     // сам вышел, или шкала кончилась
        Cart = 1,       // предмет положили в тележку
        Extraction = 2, // предмет внесли в зону экстракшена
        Destroyed = 3,  // предмет погиб: смертельная зона или таран. Шкала в ноль
        RoundOver = 4,  // уровень закончился, игрока воскресили, вывал за карту
    }

    /// <summary>
    /// Одно вселение. Компонент висит на том же GameObject, что и PlayerAvatar
    /// с его PhotonView, — поэтому RPC адресуются по игроку бесплатно.
    ///
    /// Кто что делает (раздел 10 диздока):
    ///   мастер   — выбирает предмет, держит щит, прикладывает силы, следит
    ///              за тележкой, экстракшеном, высотой и гибелью предмета;
    ///   владелец — читает ввод, считает шкалу, ведёт камеру и HUD;
    ///   все      — свечение и дрожь.
    ///
    /// Игра уже занимает слово "Possess" под принудительные реплики в чат
    /// (ChatManager.PossessChat), поэтому классы мода зовутся Iam* (13.4).
    /// </summary>
    internal sealed class Possession : MonoBehaviour
    {
        public static readonly List<Possession> All = new List<Possession>();
        public static Possession Local;

        internal PlayerAvatar avatar;
        private PhotonView view;

        // ---- общее состояние, есть у всех клиентов ----
        public PhysGrabObject Target;
        public ValuableObject Valuable;
        public ItemClass Class;
        public bool HeldByPlayer;
        public bool Active => Target != null;

        // ---- щит: исходные значения, чтобы вернуть ровно их (13.4) ----
        private bool shieldApplied;
        // Пока предмет в руках живого и ещё пару секунд после — щита нет.
        private float grabOpenTimer;
        private bool savedDestroyDisable;
        private bool savedPlayerHurtDisable;

        // ---- ввод: локальный клиент шлёт, мастер читает ----
        private bool jumpHeld;
        private Vector2 moveInput;   // WASD, у мастера
        private Vector2 moveSent;    // что уже отправлено, у владельца

        // ---- только мастер ----
        private float charge;
        private float jumpCooldown;
        private bool grounded;
        private float groundCheckTimer;
        private float crawlGrace;   // пока катимся, удары о стены не стоят шкалы
        private float pendingImpulse;
        private float hitCooldown;      // пауза между уроном от разгона
        private Vector3 lastVelocity;   // скорость до столкновения, для урона
        private ImpactRelay relay;
        private float extractionTimer;
        private bool grabbedPrevious;
        private UnityAction onLight, onMedium, onHeavy;

        // ---- только владелец ----
        public float ChargeVisual;   // для индикатора силы, приходит с мастера
        public float HitFlash;       // короткая вспышка свечения после удара
        private float actionCooldown; // общий кулдаун на шум и стук
        private float rattleCooldown;
        private bool stopRequested;

        // ---- все клиенты ----
        private readonly Glow glow = new Glow();
        private Quaternion shakeTarget = Quaternion.identity;
        private float shakeCooldown;
        private Transform shakeTransform;
        private Quaternion shakeOriginal;

        // =====================================================================
        //  Жизненный цикл
        // =====================================================================

        private void Awake()
        {
            avatar = GetComponent<PlayerAvatar>();
            view = GetComponent<PhotonView>();
            All.Add(this);
        }

        private void Start()
        {
            if (avatar && avatar.isLocal) Local = this;
        }

        private void OnDestroy()
        {
            // Игрок отключился прямо во вселении — предмет не должен остаться
            // бессмертным (13.4).
            RestoreShield();
            if (relay) Object.Destroy(relay);
            relay = null;
            glow.Detach();
            RestoreShake();
            UnsubscribeImpacts();
            All.Remove(this);
            if (Local == this) Local = null;
        }

        public static bool IsPossessed(PhysGrabObject pgo)
        {
            foreach (var p in All)
                if (p.Target == pgo) return true;
            return false;
        }

        public static Possession Of(PhysGrabObject pgo)
        {
            foreach (var p in All)
                if (p.Target == pgo) return p;
            return null;
        }

        // =====================================================================
        //  Шкала стабильности (раздел 5). Живёт только у владельца.
        // =====================================================================

        /// <summary>
        /// Ванильная энергия головы. Мод её больше не тратит на действия —
        /// она только пропуск на вход: копится сама, вселение её списывает.
        /// </summary>
        public static float Energy
        {
            get => SpectateCamera.instance ? SpectateCamera.instance.headEnergy : 0f;
            set { if (SpectateCamera.instance) SpectateCamera.instance.headEnergy = Mathf.Clamp01(value); }
        }

        /// <summary>
        /// Выносливость внутри предмета. Своя, отдельная от энергии головы:
        /// на общей шкале один прыжок съедал пятую часть вселения и играть
        /// было нечем. Полная при входе, тратится только на действия.
        /// </summary>
        public float Stamina;

        /// <summary>Выносливость своего вселения, для полосы на экране.</summary>
        public static float LocalStamina => Local ? Local.Stamina : 0f;

        /// <summary>
        /// Сколько секунд держит полная выносливость, если ничего не делать.
        /// Апгрейд батарейки продлевает это время, а с ним и число прыжков.
        /// </summary>
        public static float Duration()
        {
            var me = PlayerController.instance ? PlayerController.instance.playerAvatarScript : null;
            return Balance.DurationFor(Cfg.PossessionDuration.Value, me ? me.upgradeDeathHeadBattery : 0f);
        }

        private void Spend(float amount)
        {
            Stamina = Mathf.Clamp01(Stamina - amount);
            if (Stamina <= 0f) RequestStop();
        }

        // =====================================================================
        //  Вход
        // =====================================================================

        /// <summary>
        /// Всё готово для вселения, кроме, может быть, шкалы. Отдельно от
        /// CanPossess, чтобы подсказка на экране могла сказать, что мешает
        /// именно шкала.
        /// </summary>
        public static bool PossessReady()
        {
            if (!Cfg.Enabled.Value) return false;
            if (!SemiFunc.RunIsLevel()) return false;

            var cam = SpectateCamera.instance;
            if (!cam) return false;
            if (cam.CheckState(SpectateCamera.State.Head)) return false;
            if (!cam.CheckState(SpectateCamera.State.Normal)) return false;

            var me = Local;
            if (!me || me.Active) return false;
            if (!me.avatar || !me.avatar.isDisabled) return false;

            return true;
        }

        public static bool CanPossess()
        {
            return PossessReady() && Energy >= Cfg.MinEnergyToPossess.Value;
        }

        public void RequestPossess()
        {
            if (SemiFunc.IsMultiplayer()) view.RPC("IamRequestRPC", RpcTarget.MasterClient);
            else IamRequestRPC();
        }

        [PunRPC]
        private void IamRequestRPC(PhotonMessageInfo info = default)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!SemiFunc.OwnerOnlyRPC(info, view)) return;
            if (Active) return;

            var target = Targets.PickRandom();

            // Страховка. Отбор уже исключает занятые предметы, и мастер
            // проставляет Target синхронно внутри этого же вызова, поэтому
            // двоим один предмет достаться не может. Но если это когда-нибудь
            // сломается, два призрака в одном предмете дерутся за физику и
            // навсегда оставляют его неуязвимым — лучше отказать.
            if (target && IsPossessed(target))
            {
                Plugin.Log.LogWarning("Picked an already possessed item, refusing.");
                target = null;
            }

            if (!target)
            {
                if (SemiFunc.IsMultiplayer()) view.RPC("IamDeniedRPC", view.Owner);
                else IamDeniedRPC();
                return;
            }

            if (SemiFunc.IsMultiplayer()) view.RPC("IamStartRPC", RpcTarget.All, target.photonView.ViewID);
            else ApplyStart(target);
        }

        [PunRPC]
        private void IamDeniedRPC(PhotonMessageInfo info = default)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            Hud.Message("Nothing to possess", 2f);
        }

        [PunRPC]
        private void IamStartRPC(int viewID, PhotonMessageInfo info = default)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            var pv = PhotonView.Find(viewID);
            if (!pv) return;
            ApplyStart(pv.GetComponent<PhysGrabObject>());
        }

        private void ApplyStart(PhysGrabObject target)
        {
            if (!target) return;
            var valuable = target.GetComponent<ValuableObject>();
            if (!valuable) return;

            Target = target;
            Valuable = valuable;
            Class = Targets.ClassOf(valuable);
            HeldByPlayer = target.grabbed;
            charge = 0f;
            jumpCooldown = 0f;
            grounded = false;
            groundCheckTimer = 0f;
            crawlGrace = 0f;
            pendingImpulse = 0f;
            hitCooldown = 0f;
            lastVelocity = Vector3.zero;
            HitFlash = 0f;
            extractionTimer = 0f;
            grabbedPrevious = false;
            stopRequested = false;
            grabOpenTimer = target.grabbed ? Cfg.GrabShieldDelay.Value : 0f;
            moveInput = Vector2.zero;
            moveSent = Vector2.zero;
            Stamina = 1f;

            ApplyShield();
            SetupGlow();
            shakeTransform = FindShakeTransform(target);
            if (shakeTransform) shakeOriginal = shakeTransform.localRotation;

            Effects.Teleport(target.centerPoint);
            Effects.PlayEquip(target.centerPoint);

            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                SubscribeImpacts();
                // Столкновения Unity шлёт на объект с Rigidbody. Свой слушатель
                // точнее любых проверок по расстоянию: сразу известно, во что
                // именно врезались.
                if (target.rb) relay = target.rb.gameObject.AddComponent<ImpactRelay>();
                if (relay) relay.owner = this;
            }

            if (this == Local)
            {
                Energy -= Cfg.EntryCost.Value;
                GhostCamera.Enter();
            }

            Plugin.Log.LogInfo($"{avatar?.playerName} possessed {valuable.name} ({Class})");
        }

        // =====================================================================
        //  Выход
        // =====================================================================

        /// <summary>Владелец просит мастера выбросить его из предмета.</summary>
        public void RequestStop()
        {
            if (!Active || stopRequested) return;
            stopRequested = true;
            if (SemiFunc.IsMasterClientOrSingleplayer()) Stop(StopReason.Manual);
            else view.RPC("IamStopRequestRPC", RpcTarget.MasterClient);
        }

        [PunRPC]
        private void IamStopRequestRPC(PhotonMessageInfo info = default)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!SemiFunc.OwnerOnlyRPC(info, view)) return;
            Stop(StopReason.Manual);
        }

        /// <summary>Только мастер. Рассылает всем и завершает вселение.</summary>
        public void Stop(StopReason reason)
        {
            if (!Active) return;
            int speakerViewID = 0;
            if ((reason == StopReason.Cart || reason == StopReason.Destroyed) &&
                Target && Target.lastPlayerGrabbing && Target.lastPlayerGrabbing.photonView)
            {
                speakerViewID = Target.lastPlayerGrabbing.photonView.ViewID;
            }

            if (SemiFunc.IsMultiplayer()) view.RPC("IamStopRPC", RpcTarget.All, (byte)reason, speakerViewID);
            else ApplyStop((byte)reason, speakerViewID);
        }

        [PunRPC]
        private void IamStopRPC(byte reason, int speakerViewID, PhotonMessageInfo info = default)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            ApplyStop(reason, speakerViewID);
        }

        private void ApplyStop(byte reasonByte, int speakerViewID)
        {
            if (!Active) return;
            var reason = (StopReason)reasonByte;

            Vector3 where = Target ? Target.centerPoint : transform.position;

            UnsubscribeImpacts();
            if (relay) Object.Destroy(relay);
            relay = null;

            // Щит не снимаем прямо здесь. Предмет после выселения ещё летит и
            // катится, и разбивался ровно в тот кадр, когда защита уходила.
            // Передаём щит ExitGrace: он вернёт исходные значения сам, когда
            // предмет остановится. Снимать и тут же ставить заново нельзя —
            // между этими двумя строчками успевает пройти шаг физики.
            bool shieldHandedOver = false;
            if (reason != StopReason.Destroyed && shieldApplied && Target && Target.impactDetector)
            {
                ExitGrace.Add(Target, savedDestroyDisable, savedPlayerHurtDisable);
                shieldApplied = false;
                shieldHandedOver = true;
            }
            if (!shieldHandedOver) RestoreShield();

            glow.Detach();
            RestoreShake();

            Target = null;
            Valuable = null;
            HeldByPlayer = false;
            jumpHeld = false;
            ChargeVisual = 0f;

            Effects.Teleport(where);
            Effects.PlayUnequip(where);

            if (this == Local)
            {
                // Не смог сберечь тело — сиди копи (раздел 4.3).
                if (reason == StopReason.Destroyed) Stamina = 0f;
                GhostCamera.Exit();
            }

            // Прощальная фраза говорит тот живой, кто держал предмет.
            if (speakerViewID != 0 && Local != null && Local.avatar &&
                Local.avatar.photonView && Local.avatar.photonView.ViewID == speakerViewID)
            {
                if (reason == StopReason.Cart && Cfg.CartPhrasesEnabled.Value)
                    Speech.Say(Phrases.Pick(Phrases.Cart));
                else if (reason == StopReason.Destroyed && Cfg.BreakPhrasesEnabled.Value)
                    Speech.Say(Phrases.Pick(Phrases.Break));
            }
        }

        // =====================================================================
        //  Щит (раздел 7.2)
        // =====================================================================

        private void ApplyShield()
        {
            if (shieldApplied || !Target || !Target.impactDetector) return;

            // Предмет мог остаться под защитой прошлого выселения. Сначала
            // возвращаем его в исходное состояние, иначе запомним щит как
            // «родное» значение и уже никогда его не снимем.
            ExitGrace.Release(Target);

            var impact = Target.impactDetector;

            savedDestroyDisable = impact.destroyDisable;
            savedPlayerHurtDisable = impact.playerHurtDisable;
            shieldApplied = true;

            UpdateShield();
            // destroyDisableTeleport не трогаем: это страховка игры от потери
            // лута при провале сквозь геометрию, а не лазейка (раздел 7.5).
        }

        /// <summary>
        /// Щит на этот кадр. Снимают его две вещи: осознанный таран и чужие
        /// руки — пока живой несёт предмет, тот бьётся по правилам игры,
        /// иначе вселение делает ценность неуязвимой на весь путь до тележки.
        /// </summary>
        private void UpdateShield()
        {
            if (!shieldApplied || !Target || !Target.impactDetector) return;
            var impact = Target.impactDetector;

            bool inHands = grabOpenTimer > 0f;

            // Пока призрак внутри, предмет денег не теряет вообще. Урон по
            // монстрам и живым мод наносит сам, ванильным правилам ломания он
            // больше не подчинён — потому и щит теперь безусловный.
            impact.destroyDisable = Cfg.ValuableShield.Value && !inHands
                ? true
                : savedDestroyDisable;

            // Ванильный урон живым от предмета мод не использует: он завязан
            // на то же destroyDisable. Держим выключенным, бьём сами.
            impact.playerHurtDisable = savedPlayerHurtDisable || Cfg.ValuableShield.Value;
        }

        private void RestoreShield()
        {
            if (!shieldApplied) return;
            shieldApplied = false;
            if (!Target || !Target.impactDetector) return;
            Target.impactDetector.destroyDisable = savedDestroyDisable;
            Target.impactDetector.playerHurtDisable = savedPlayerHurtDisable;
        }

        /// <summary>Раздел 7.4: смертельная зона пробивает щит. Только мастер.</summary>
        public void KillInDeathPit()
        {
            if (!Active || !SemiFunc.IsMasterClientOrSingleplayer()) return;
            var impact = Target.impactDetector;
            RestoreShield();
            Stop(StopReason.Destroyed);
            if (impact) impact.DestroyObject();
        }

        // =====================================================================
        //  Действия владельца
        // =====================================================================

        private void SendAction(byte action)
        {
            if (SemiFunc.IsMultiplayer()) view.RPC("IamActionRPC", RpcTarget.MasterClient, action);
            else IamActionRPC(action);
        }

        [PunRPC]
        private void IamActionRPC(byte action, PhotonMessageInfo info = default)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!SemiFunc.OwnerOnlyRPC(info, view)) return;
            if (!Active) return;

            switch (action)
            {
                case 0: DoRattle(); break;
                case 2: DoKnock(); break;
            }
        }

        private void DoRattle()
        {
            float radius;
            switch (Class)
            {
                case ItemClass.Light: radius = Cfg.RattleRadiusLight.Value; break;
                case ItemClass.Medium: radius = Cfg.RattleRadiusMedium.Value; break;
                default: radius = Cfg.RattleRadiusHeavy.Value; break;
            }
            SemiFunc.EnemyInvestigate(Target.centerPoint, radius);
            Target.rb.AddTorque(Random.insideUnitSphere * (Target.rb.mass * 1.5f), ForceMode.Impulse);
            if (SemiFunc.IsMultiplayer())
            {
                view.RPC("IamNoiseRPC", RpcTarget.All, false);
                view.RPC("IamRattleSaidRPC", view.Owner, radius);
            }
            else
            {
                IamNoiseRPC(false);
                IamRattleSaidRPC(radius);
            }
        }

        private void DoKnock()
        {
            Target.rb.AddTorque(Random.insideUnitSphere * (Target.rb.mass * 0.15f), ForceMode.Impulse);
            if (SemiFunc.IsMultiplayer()) view.RPC("IamNoiseRPC", RpcTarget.All, true);
            else IamNoiseRPC(true);
        }

        /// <summary>
        /// Шум сам по себе на экране никак не виден, и понять, сработала ли
        /// кнопка, нельзя. Говорим владельцу прямым текстом.
        /// </summary>
        [PunRPC]
        private void IamRattleSaidRPC(float radius, PhotonMessageInfo info = default)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            Hud.Message("Rattle - monsters within " + Mathf.RoundToInt(radius) + " m heard it", 1.5f);
        }

        [PunRPC]
        private void IamNoiseRPC(bool quiet, PhotonMessageInfo info = default)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            if (!Active || !Target || !Target.impactDetector) return;
            var audio = Target.impactDetector.impactAudio;
            var clip = audio ? (quiet ? audio.impactLight : audio.impactMedium) : null;
            if (clip != null) clip.Play(Target.centerPoint, quiet ? 0.4f : 1f);
            shakeCooldown = 0f;
        }

        /// <summary>Мастер списывает шкалу у владельца: удары и попадания по монстру.</summary>
        private void SpendRemote(float amount)
        {
            if (amount <= 0f) return;
            if (SemiFunc.IsMultiplayer()) view.RPC("IamSpendRPC", view.Owner, amount);
            else IamSpendRPC(amount);
        }

        [PunRPC]
        private void IamSpendRPC(float amount, PhotonMessageInfo info = default)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            if (this != Local || !Active) return;
            Spend(amount);
        }

        /// <summary>Мастер сообщает всем, что предмет взяли или отпустили.</summary>
        [PunRPC]
        private void IamGrabbedRPC(bool held, PhotonMessageInfo info = default)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            HeldByPlayer = held;
        }

        [PunRPC]
        private void IamChargeRPC(float amount, PhotonMessageInfo info = default)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            ChargeVisual = amount;
        }

        /// <summary>Мастер сообщает всем, что предмет во что-то врезался: вспышка.</summary>
        [PunRPC]
        private void IamHitRPC(PhotonMessageInfo info = default)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            HitFlash = 0.35f;
            shakeCooldown = 0f;
        }

        [PunRPC]
        private void IamMoveRPC(float x, float y, PhotonMessageInfo info = default)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!SemiFunc.OwnerOnlyRPC(info, view)) return;
            moveInput = new Vector2(x, y);
        }

        [PunRPC]
        private void IamInputRPC(bool held, PhotonMessageInfo info = default)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!SemiFunc.OwnerOnlyRPC(info, view)) return;
            jumpHeld = held;
        }

        // =====================================================================
        //  Попадание по монстру (раздел 7.3). Зовётся из патча EnemyHealth.Hurt.
        // =====================================================================

        /// <summary>
        /// Столкновение вселённого предмета с монстром или живым. Зовётся из
        /// ImpactRelay, только у мастера.
        ///
        /// Ванильный урон предметом здесь не годится: движок разрешает его
        /// только предмету без защиты, а защита нам нужна постоянно. Поэтому
        /// урон мод считает сам, по скорости — оружие здесь разгон, отдельной
        /// клавиши нет.
        /// </summary>
        internal void RamHit(Collision collision)
        {
            if (!Active || !Cfg.RamEnabled.Value) return;
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (hitCooldown > 0f) return;
            if (Target.playerGrabbing.Count > 0) return;   // в чужих руках это не наш удар

            float speed = lastVelocity.magnitude;
            if (speed < Cfg.RamMinSpeed.Value) return;

            float power = Mathf.InverseLerp(Cfg.RamMinSpeed.Value,
                                            Mathf.Max(Cfg.RamFullSpeed.Value, Cfg.RamMinSpeed.Value + 0.01f),
                                            speed);
            if (power <= 0f) return;

            float scale = Tuning.For(Class).ramScale;
            var hit = collision.transform;
            Vector3 point = collision.contacts.Length > 0 ? collision.contacts[0].point : Target.centerPoint;

            if (hit.CompareTag("Enemy") && TryHurtEnemy(hit, power * scale)) { HitDone(); return; }
            if (Cfg.RamHurtsPlayers.Value && TryHurtPlayer(hit, power * scale, point)) HitDone();
        }

        private void HitDone()
        {
            hitCooldown = Cfg.RamHitCooldown.Value;
            HitFlash = 0.35f;
            shakeCooldown = 0f;
            if (SemiFunc.IsMultiplayer()) view.RPC("IamHitRPC", RpcTarget.Others);
        }

        private bool TryHurtEnemy(Transform hit, float amount)
        {
            var body = hit.GetComponent<EnemyRigidbody>();
            if (!body) body = hit.GetComponentInParent<EnemyRigidbody>();
            if (!body || !body.enemy || !body.enemy.HasHealth) return false;

            var health = body.enemy.Health;
            // Игра сама помечает, какие монстры уязвимы для предметов и
            // насколько. Не спорим с ней.
            if (!health.objectHurt || health.dead) return false;

            int damage = Mathf.RoundToInt(Cfg.RamDamage.Value * amount * health.objectHurtMultiplier);
            if (damage <= 0) return false;

            health.Hurt(damage, (body.transform.position - Target.centerPoint).normalized);
            return true;
        }

        private bool TryHurtPlayer(Transform hit, float amount, Vector3 point)
        {
            var tumble = hit.GetComponent<PlayerTumble>();
            if (!tumble) tumble = hit.GetComponentInParent<PlayerTumble>();

            var player = tumble ? tumble.playerAvatar : hit.GetComponentInParent<PlayerAvatar>();
            if (!player)
            {
                var pusher = hit.GetComponent<PlayerPhysPusher>();
                if (pusher) player = pusher.Player;
            }
            if (!player || player == avatar || player.isDisabled || player.deadSet) return false;
            if (!player.playerHealth || player.playerHealth.health <= 0) return false;

            int damage = Mathf.RoundToInt(Cfg.RamPlayerDamage.Value * amount);
            if (damage > 0) player.playerHealth.HurtOther(damage, point, savingGrace: true);

            if (Cfg.RamTumblesPlayers.Value && player.tumble && !player.tumble.isTumbling)
            {
                player.tumble.TumbleRequest(_isTumbling: true, _playerInput: false);
                player.tumble.TumbleOverrideTime(1f);
            }
            return true;
        }

        // =====================================================================
        //  Подписка на удары (раздел 5.1). Только мастер.
        // =====================================================================

        private void SubscribeImpacts()
        {
            var impact = Target ? Target.impactDetector : null;
            if (!impact) return;

            // Катящийся предмет чиркает углами о пол и стены не переставая.
            // Платить за это нечестно: шкала утекала за само движение, а не
            // за ошибку игрока.
            onLight = () => { if (crawlGrace <= 0f) SpendRemote(Cfg.WallHitCostLight.Value); };
            onMedium = () => { if (crawlGrace <= 0f) SpendRemote(Cfg.WallHitCostMedium.Value); };
            onHeavy = () => { if (crawlGrace <= 0f) SpendRemote(Cfg.WallHitCostHeavy.Value); };

            impact.onImpactLight.AddListener(onLight);
            impact.onImpactMedium.AddListener(onMedium);
            impact.onImpactHeavy.AddListener(onHeavy);
        }

        private void UnsubscribeImpacts()
        {
            var impact = Target ? Target.impactDetector : null;
            if (impact)
            {
                if (onLight != null) impact.onImpactLight.RemoveListener(onLight);
                if (onMedium != null) impact.onImpactMedium.RemoveListener(onMedium);
                if (onHeavy != null) impact.onImpactHeavy.RemoveListener(onHeavy);
            }
            onLight = onMedium = onHeavy = null;
        }

        // =====================================================================
        //  Тик: мастер сторожит, владелец шлёт ввод, все светятся.
        //  Зовётся из Hud, а не из Unity: игра выключает GameObject мёртвого
        //  игрока (PlayerAvatar.PlayerDeathDone), вместе с ним замирают
        //  Update и FixedUpdate всех его компонентов, включая наш.
        // =====================================================================

        internal void Tick()
        {
            if (!Active) return;

            if (!Target || !Target.gameObject.activeInHierarchy)
            {
                // Предмет исчез вообще: уходим тихо и без штрафа.
                if (SemiFunc.IsMasterClientOrSingleplayer()) Stop(StopReason.Destroyed);
                else ApplyStop((byte)StopReason.Destroyed, 0);
                return;
            }

            if (HeldByPlayer) grabOpenTimer = Cfg.GrabShieldDelay.Value;
            else if (grabOpenTimer > 0f) grabOpenTimer -= Time.deltaTime;
            UpdateShield();

            if (HitFlash > 0f) HitFlash -= Time.deltaTime;
            glow.Tick(HitFlash > 0f);
            TickShake();

            if (SemiFunc.IsMasterClientOrSingleplayer()) MasterUpdate();
            if (this == Local) OwnerUpdate();
        }

        private void MasterUpdate()
        {
            // Уровень кончился или игрока воскресили.
            if (!SemiFunc.RunIsLevel() || !avatar || !avatar.isDisabled)
            {
                Stop(StopReason.RoundOver);
                return;
            }

            var impact = Target.impactDetector;

            // Тележка — мгновенный выброс (раздел 8.4).
            if (impact && impact.inCart)
            {
                Stop(StopReason.Cart);
                return;
            }

            // Экстракшен — предупреждение и выброс через 2 секунды (8.5).
            if (Valuable && Valuable.roomVolumeCheck && Valuable.roomVolumeCheck.inExtractionPoint)
            {
                extractionTimer += Time.deltaTime;
                if (SemiFunc.PerSecond(4f, this))
                {
                    if (SemiFunc.IsMultiplayer()) view.RPC("IamWarnRPC", view.Owner);
                    else IamWarnRPC();
                }
                if (extractionTimer >= 2f)
                {
                    Stop(StopReason.Extraction);
                    return;
                }
            }
            else
            {
                extractionTimer = 0f;
            }

            // Вывал за карту: убираем за собой до отметки -50, дальше игра
            // отрабатывает ровно как без мода (раздел 7.5).
            if (Target.transform.position.y < -45f)
            {
                Stop(StopReason.RoundOver);
                return;
            }

            // Кто-то взял предмет в руки.
            bool held = Target.playerGrabbing.Count > 0;
            if (held != grabbedPrevious)
            {
                grabbedPrevious = held;
                HeldByPlayer = held;
                if (SemiFunc.IsMultiplayer()) view.RPC("IamGrabbedRPC", RpcTarget.Others, held);
            }

            if (hitCooldown > 0f) hitCooldown -= Time.deltaTime;

            if (jumpCooldown > 0f) jumpCooldown -= Time.deltaTime;
            if (crawlGrace > 0f) crawlGrace -= Time.deltaTime;

            // Опора проверяется пять раз в секунду, а не каждый кадр: на
            // неровном полу луч мигает. Так же считает ванильная голова
            // (PlayerDeathHead.spectatedJumpGroundedTimer).
            if (groundCheckTimer > 0f) groundCheckTimer -= Time.deltaTime;
            else
            {
                groundCheckTimer = 0.2f;
                grounded = SemiFunc.OnGroundCheck(Target.centerPoint, 0.5f, Target);
            }

            // Зарядка прыжка или рывка. В руках у живого вырваться нельзя (8.1).
            var tuning = Tuning.For(Class);
            if (jumpHeld && !held && charge <= tuning.chargeMax)
            {
                // Нет опоры или идёт откат — заряд просто стоит на месте.
                // Раньше эти два условия стояли снаружи, и в такой момент заряд
                // срывался в микропрыжок, включая откат на две секунды: дальше
                // нажатия пробела пропадали одно за другим.
                if (grounded && jumpCooldown <= 0f) charge += Time.deltaTime;
            }
            else if (charge > 0f)
            {
                // Короткий тычок по пробелу обязан давать заметный прыжок,
                // иначе нажатие выглядит потерянным.
                pendingImpulse = Mathf.Max(charge, tuning.minCharge);
                charge = 0f;
                jumpCooldown = tuning.cooldown;
                grounded = false;
                groundCheckTimer = 0f;
                // Платит мастер, а не владелец: только здесь точно известно,
                // что прыжок действительно состоялся.
                SpendRemote(Cfg.JumpCost.Value);
            }

            // Заряд нужен всем клиентам: от него зависит амплитуда дрожи.
            // Шлём редко и только пока он меняется — RPC в Photon надёжные,
            // сыпать ими каждый кадр незачем.
            float chargeNormalized = charge / tuning.chargeMax;
            bool chargeChanged = !Mathf.Approximately(chargeNormalized, ChargeVisual);
            ChargeVisual = chargeNormalized;
            if (SemiFunc.IsMultiplayer() && chargeChanged && SemiFunc.PerSecond(5f, this))
                view.RPC("IamChargeRPC", RpcTarget.Others, chargeNormalized);
        }

        [PunRPC]
        private void IamWarnRPC(PhotonMessageInfo info = default)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            Hud.Message("Extraction point. Ejecting.", 0.5f);
        }

        private void OwnerUpdate()
        {
            if (!Active) return;   // мастер мог выбросить нас в этом же кадре
            if (actionCooldown > 0f) actionCooldown -= Time.deltaTime;
            if (rattleCooldown > 0f) rattleCooldown -= Time.deltaTime;

            // Меню открыто — ввод не читаем.
            if (MenuManager.instance && MenuManager.instance.currentMenuPage) return;
            if (ChatManager.instance && !ChatManager.instance.StateIsInactive() &&
                !ChatManager.instance.StateIsPossessed()) return;

            // Выход по клавише.
            if (SemiFunc.InputDown(InputKey.Interact))
            {
                RequestStop();
                return;
            }

            // Пробел: зарядка прыжка или рывка. В руках у живого заблокировано.
            bool wantJump = !HeldByPlayer && SemiFunc.InputHold(InputKey.Jump);
            if (wantJump != jumpHeld)
            {
                jumpHeld = wantJump;
                if (SemiFunc.IsMultiplayer()) view.RPC("IamInputRPC", RpcTarget.MasterClient, jumpHeld);
            }

            // WASD: ползание. Шлём только смену направления, восемь на круг.
            Vector2 move = SemiFunc.InputMovement();
            Vector2 rounded = new Vector2(
                Mathf.Clamp(Mathf.Round(move.x), -1f, 1f),
                Mathf.Clamp(Mathf.Round(move.y), -1f, 1f));
            if (HeldByPlayer) rounded = Vector2.zero;
            if (rounded != moveSent)
            {
                moveSent = rounded;
                if (SemiFunc.IsMultiplayer())
                    view.RPC("IamMoveRPC", RpcTarget.MasterClient, rounded.x, rounded.y);
                else
                    moveInput = rounded;
            }

            // ЛКМ: шум, или стук-сигнал если предмет в руках у живого (8.3).
            if (SemiFunc.InputDown(InputKey.Grab) && actionCooldown <= 0f)
            {
                if (HeldByPlayer)
                {
                    actionCooldown = 1f;
                    Spend(Cfg.KnockCost.Value);
                    SendAction(2);
                }
                else if (rattleCooldown <= 0f)
                {
                    actionCooldown = 0.5f;
                    rattleCooldown = 4f;
                    Spend(Cfg.RattleCost.Value);
                    SendAction(0);
                }
            }

            if (!SemiFunc.IsMasterClientOrSingleplayer())
            {
                // Локальная оценка заряда, чтобы индикатор не ждал сети.
                var tuning = Tuning.For(Class);
                ChargeVisual = jumpHeld
                    ? Mathf.Min(1f, ChargeVisual + Time.deltaTime / tuning.chargeMax)
                    : 0f;
            }

            // Само присутствие в предмете тоже стоит выносливости, иначе
            // выгоднее всего просто сидеть кружкой до конца забега.
            Spend(Time.deltaTime / Duration());
        }

        // =====================================================================
        //  Физика, только мастер (раздел 6). Тоже зовётся из Hud.
        // =====================================================================

        internal void PhysicsTick()
        {
            if (!Active || !SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!Target || !Target.rb) return;

            var tuning = Tuning.For(Class);
            var rb = Target.rb;
            bool held = Target.playerGrabbing.Count > 0;

            // FixedUpdate идёт до расчёта столкновений, поэтому здесь ещё
            // видна скорость, с которой предмет во что-то влетит.
            lastVelocity = rb.velocity;
            bool crawling = !held && moveInput.sqrMagnitude > 0.01f &&
                            Cfg.CrawlSpeed.Value > 0f && grounded;

            // Доворот по камере, как у ванильной головы. Пока едем на WASD,
            // доворота нет: он держал предмет за нужный угол и не давал ему
            // катиться, из-за чего ползание выглядело покачиванием на месте.
            if (!held && !crawling)
            {
                var camera = avatar && avatar.localCamera ? avatar.localCamera.GetOverrideTransform() : null;
                if (camera)
                {
                    Quaternion wanted;
                    if (Class == ItemClass.Heavy)
                    {
                        // Только по горизонтали, иначе шкаф начнёт вертеться (6.4).
                        var e = Target.transform.rotation.eulerAngles;
                        wanted = Quaternion.Euler(e.x, camera.rotation.eulerAngles.y, e.z);
                    }
                    else
                    {
                        wanted = camera.rotation;
                    }
                    Vector3 torque = SemiFunc.PhysFollowRotation(Target.transform, wanted, rb, tuning.followMaxSpeed);
                    torque = Vector3.Lerp(Vector3.zero, torque, tuning.followStrength * Time.fixedDeltaTime);
                    rb.AddTorque(torque, ForceMode.Impulse);
                    Target.OverrideTorqueStrength(0f);
                }
            }

            // Ползание на WASD. Раньше это была постоянная слабая сила: на
            // ценностях она не пересиливала даже трение покоя, и предмет
            // только качался. Теперь мод разгоняет предмет до нужной скорости
            // и подкручивает его, чтобы он катился.
            if (crawling)
            {
                var camera = avatar && avatar.localCamera ? avatar.localCamera.GetOverrideTransform() : null;
                if (camera)
                {
                    Vector3 forward = camera.forward; forward.y = 0f;
                    Vector3 right = camera.right; right.y = 0f;
                    Vector3 direction = forward.normalized * moveInput.y + right.normalized * moveInput.x;

                    if (direction.sqrMagnitude > 0.0001f)
                    {
                        direction.Normalize();
                        crawlGrace = 0.5f;

                        // Потолок скорости у ползания свой, иначе им можно было
                        // бы разогнаться не хуже прыжка и прыжок стал бы не нужен.
                        float speed = Cfg.CrawlSpeed.Value * tuning.crawlScale;
                        var flat = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
                        Vector3 delta = direction * speed - flat;

                        rb.AddForce(Vector3.ClampMagnitude(delta * Cfg.CrawlAcceleration.Value,
                                                           Cfg.CrawlAcceleration.Value) * rb.mass,
                                    ForceMode.Force);

                        // Момент поперёк движения: без него ползание выглядит
                        // как скольжение ящика, а не как катящийся предмет.
                        if (Cfg.CrawlSpin.Value > 0f)
                            rb.AddTorque(Vector3.Cross(Vector3.up, direction) * (Cfg.CrawlSpin.Value * rb.mass),
                                         ForceMode.Force);
                    }
                }
            }

            // Прыжок или рывок.
            if (pendingImpulse > 0f)
            {
                var camera = avatar && avatar.localCamera ? avatar.localCamera.GetOverrideTransform() : null;
                if (camera && !held)
                {
                    Vector3 direction = camera.forward;
                    if (Class == ItemClass.Heavy && Cfg.HeavySlideOnly.Value)
                    {
                        direction.y = 0f;                       // рояль не летает (6.4)
                        if (direction.sqrMagnitude < 0.0001f) direction = camera.up;
                        direction.Normalize();
                    }
                    rb.AddForce(direction * (tuning.force * pendingImpulse * rb.mass), ForceMode.Impulse);
                }
                pendingImpulse = 0f;
            }

            // Потолок скорости.
            if (!held && rb.velocity.magnitude > tuning.speedCap)
                rb.velocity = rb.velocity.normalized * tuning.speedCap;
        }

        // =====================================================================
        //  Дрожь и свечение
        // =====================================================================

        private void SetupGlow()
        {
            var color = Color.cyan;
            var cosmetics = avatar ? avatar.playerAvatarVisuals : null;
            if (cosmetics && cosmetics.playerCosmetics && MetaManager.instance != null &&
                MetaManager.instance.colors != null && MetaManager.instance.colors.Count > 0)
            {
                var equipped = cosmetics.playerCosmetics.colorsEquipped;
                int slot = (int)SemiFunc.CosmeticType.BodyTopMesh;
                if (equipped != null && equipped.Length > slot)
                {
                    int index = Mathf.Clamp(equipped[slot], 0, MetaManager.instance.colors.Count - 1);
                    var semiColor = MetaManager.instance.colors[index];
                    if (semiColor) color = semiColor.color;
                }
            }
            glow.BaseColor = color;
            glow.Attach(Target.gameObject);
        }

        private static Transform FindShakeTransform(PhysGrabObject pgo)
        {
            // Крутим первый попавшийся дочерний меш, чтобы не дёргать сам
            // Rigidbody: дрожь чисто визуальная, физику она трогать не должна.
            var renderer = pgo.GetComponentInChildren<MeshRenderer>();
            if (renderer && renderer.transform != pgo.transform) return renderer.transform;
            return null;
        }

        private void TickShake()
        {
            if (!shakeTransform) return;

            float amount = 0.6f + ChargeVisual * 3f;
            if (shakeCooldown <= 0f)
            {
                shakeTarget = shakeOriginal * Quaternion.Euler(
                    Random.Range(-amount, amount),
                    Random.Range(-amount, amount),
                    Random.Range(-amount, amount));
                shakeCooldown = Random.Range(0.01f, 0.05f);
            }
            else
            {
                shakeCooldown -= Time.deltaTime;
            }
            shakeTransform.localRotation = Quaternion.Slerp(shakeTransform.localRotation, shakeTarget, 20f * Time.deltaTime);
        }

        private void RestoreShake()
        {
            if (shakeTransform) shakeTransform.localRotation = shakeOriginal;
            shakeTransform = null;
            shakeTarget = Quaternion.identity;
        }
    }

    /// <summary>
    /// Защита предмета после выхода призрака. Фиксированных секунд не хватало:
    /// предмет после выселения ещё катится, и разбивался ровно тогда, когда
    /// защита кончалась. Держим её, пока он не остановится и не полежит
    /// спокойно ExitGraceSeconds, но не дольше потолка.
    ///
    /// Щит здесь не «включается заново», а принимается из вселения как есть:
    /// снять его и тут же поставить обратно нельзя, между двумя строчками
    /// успевает пройти шаг физики, и именно в него предмет и разбивался.
    /// </summary>
    internal static class ExitGrace
    {
        private sealed class Entry
        {
            public PhysGrabObject item;
            public bool destroyDisable;    // что было до вселения
            public bool playerHurtDisable;
            public float left;             // сколько ещё разрешено держать, потолок
            public float still;            // сколько секунд предмет уже стоит
        }

        private static readonly List<Entry> entries = new List<Entry>();

        public static void Add(PhysGrabObject item, bool destroyDisable, bool playerHurtDisable)
        {
            if (!item || !item.impactDetector) return;

            if (Cfg.ExitGraceSeconds.Value <= 0f)
            {
                Restore(item, destroyDisable, playerHurtDisable);
                return;
            }

            foreach (var e in entries)
                if (e.item == item) { e.left = Cfg.ExitGraceMaxSeconds.Value; e.still = 0f; return; }

            item.impactDetector.destroyDisable = true;
            item.OverrideIndestructible(Cfg.ExitGraceSeconds.Value);

            entries.Add(new Entry
            {
                item = item,
                destroyDisable = destroyDisable,
                playerHurtDisable = playerHurtDisable,
                left = Cfg.ExitGraceMaxSeconds.Value,
                still = 0f,
            });
        }

        /// <summary>Вернуть предмет игре немедленно: в него снова вселяются.</summary>
        public static void Release(PhysGrabObject item)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].item != item) continue;
                Restore(entries[i].item, entries[i].destroyDisable, entries[i].playerHurtDisable);
                entries.RemoveAt(i);
            }
        }

        public static void Tick()
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                if (!e.item || !e.item.rb || !e.item.impactDetector) { entries.RemoveAt(i); continue; }

                // Взяли в руки — дальше предмет живёт по правилам игры, иначе
                // его можно донести до тележки неуязвимым.
                bool grabbed = e.item.playerGrabbing.Count > 0;

                e.left -= Time.deltaTime;

                bool moving = e.item.rb.velocity.magnitude > 0.4f ||
                              e.item.rb.angularVelocity.magnitude > 1.5f;
                e.still = moving ? 0f : e.still + Time.deltaTime;

                if (grabbed || e.left <= 0f || e.still >= Cfg.ExitGraceSeconds.Value)
                {
                    Restore(e.item, e.destroyDisable, e.playerHurtDisable);
                    entries.RemoveAt(i);
                    continue;
                }

                e.item.impactDetector.destroyDisable = true;
                e.item.OverrideIndestructible(0.25f);
            }
        }

        private static void Restore(PhysGrabObject item, bool destroyDisable, bool playerHurtDisable)
        {
            if (!item || !item.impactDetector) return;
            item.impactDetector.destroyDisable = destroyDisable;
            item.impactDetector.playerHurtDisable = playerHurtDisable;
        }
    }

    /// <summary>
    /// Слушатель столкновений вселённого предмета. Висит на объекте с
    /// Rigidbody, потому что Unity шлёт OnCollisionEnter именно туда. Живёт
    /// только у мастера и только на время вселения.
    /// </summary>
    internal sealed class ImpactRelay : MonoBehaviour
    {
        internal Possession owner;

        private void OnCollisionEnter(Collision collision)
        {
            if (owner) owner.RamHit(collision);
        }
    }

    /// <summary>Числа управления по классам предметов, раздел 6.2-6.4.</summary>
    internal struct Tuning
    {
        public float chargeMax;
        public float minCharge;       // короткий тычок по пробелу всё равно прыгает
        public float force;
        public float crawlScale;      // доля CrawlSpeed для этого класса
        public float ramScale;        // множитель урона от разгона
        public float speedCap;
        public float cooldown;
        public float followMaxSpeed;  // 4-й аргумент SemiFunc.PhysFollowRotation
        public float followStrength;  // множитель, как num2 у ванильной головы

        public static Tuning For(ItemClass c)
        {
            switch (c)
            {
                // Зарядка короткая, а сила большая: прыжок — это выстрел, а не
                // подскок. Потолок скорости поднят следом, иначе он срезал
                // импульс в тот же кадр и прыжок выходил вялым.
                case ItemClass.Light:
                    return new Tuning { chargeMax = 0.7f, minCharge = 0.15f, force = 16f, crawlScale = 1.0f, ramScale = 0.6f, speedCap = 18f, cooldown = 0.5f, followMaxSpeed = 5f, followStrength = 10f };
                case ItemClass.Medium:
                    return new Tuning { chargeMax = 0.8f, minCharge = 0.15f, force = 13f, crawlScale = 0.85f, ramScale = 1.0f, speedCap = 15f, cooldown = 0.6f, followMaxSpeed = 5f, followStrength = 10f };
                default:
                    // Тяжёлые доворачиваются медленно и только по горизонтали (6.4).
                    return new Tuning { chargeMax = 1.0f, minCharge = 0.25f, force = 9f, crawlScale = 0.5f, ramScale = 1.6f, speedCap = 9f, cooldown = 0.9f, followMaxSpeed = 1.5f, followStrength = 3f };
            }
        }
    }
}
