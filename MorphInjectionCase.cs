using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SimpleJSON; // JSONNode

namespace JustAnotherUser {
    public class MorphInjectionCase : MVRScript {
        private MorphInjectionCase.CollisionBox _injectionCollisionBox; // area where to detect injections
        private List<MorphInjectionCase.CollisionBox> _loadCollisionBox; // area where to detect loads
        private IDictionary<FreeControllerV3,Vector3> _storedAtoms;
        private FreeControllerV3.OnGrabStart _onGrabEvent;

        private Transform _lid;
        private FreeControllerV3 _lidRotator;
        private FreeControllerV3.OnGrabEnd _releaseEvent;

        private static readonly float LID_LENGHT = 0.45f, // z-offset
                                      LID_MAX_ROTATION = 130f;
        private static readonly Vector3 LID_OFFSET = new Vector3(0f, 0.14f, -0.2f);
        private static readonly string LID_ROTATOR_NAME = "Lid_{object_id}";

        private JSONStorableFloat _collisionStorable;

        private static readonly string COLLISION_BOX_NAME = "MI_case_collision_{object_id}#{number}";
        private static readonly float COLLISION_BOX_SIZE = 0.15f;
        private static readonly Vector3 COLLISION_BOX_OFFSET = LID_OFFSET + new Vector3(0f, 0f, LID_LENGHT / 2),
                                        PLACED_INJECTOR_OFFSET = COLLISION_BOX_OFFSET + new Vector3(0.006f, -0.015f, -0.025f);
        private static readonly Vector3 []LOAD_COLLISION_BOX_OFFSETS = { COLLISION_BOX_OFFSET + new Vector3(0.193f, -0.014f, -0.025f),
                                                                        COLLISION_BOX_OFFSET + new Vector3(0.193f, -0.014f, 0.055f),
                                                                        COLLISION_BOX_OFFSET + new Vector3(0.193f, -0.014f, -0.103f),
                                                                        COLLISION_BOX_OFFSET + new Vector3(-0.193f, -0.014f, -0.025f),
                                                                        COLLISION_BOX_OFFSET + new Vector3(-0.193f, -0.014f, 0.055f),
                                                                        COLLISION_BOX_OFFSET + new Vector3(-0.193f, -0.014f, -0.103f)};

        private static readonly string VERSION = "2.3";

        public override void Init() {
            // plugin VaM GUI description
            pluginLabelJSON.val = "MorphInjection [Case] v" + VERSION;
        }

        protected void Start() {
            this._loadCollisionBox = new List<MorphInjectionCase.CollisionBox>();
            this._storedAtoms = new Dictionary<FreeControllerV3, Vector3>();

            this._releaseEvent = (controller) => OnLidChange();

            
            this._onGrabEvent = (controller) => {
                // detach
                this._storedAtoms.Remove(controller);
                controller.onGrabStartHandlers -= this._onGrabEvent;
            };

            this._collisionStorable = new JSONStorableFloat("collision", -1f, -1f, LOAD_COLLISION_BOX_OFFSETS.Length); // TODO interactuable false?
            RegisterFloat(this._collisionStorable);
            CreateSlider(this._collisionStorable);
            this._collisionStorable.slider.onValueChanged.AddListener((val) => {
                if (val < 0f) return;
                this._collisionStorable.val = -1f;

                if (val == 0f) {
                    Atom collided = getCollidingAtoms(this._injectionCollisionBox).FirstOrDefault(a => a.GetComponentsInChildren<MVRScript>().Select(script => script.GetType().FullName).Any(scriptName => scriptName == "JustAnotherUser.MorphInjection"));
                    if (collided == null) return;
                    OnCollision(collided);
                }
                else {
                    int index = (int)val - 1;
                    if (index >= this._loadCollisionBox.Count) return;
                    MorphInjectionCase.CollisionBox detectCollision = this._loadCollisionBox[index];
                    Atom collided = getCollidingAtoms(detectCollision).FirstOrDefault(a => a.GetComponentsInChildren<MVRScript>().Select(script => script.GetType().FullName).Any(scriptName => scriptName == "JustAnotherUser.MorphInjectionLoad"));
                    if (collided == null) return;
                    OnLoadCollision(collided, Quaternion.Inverse(containingAtom.mainController.transform.rotation) * (detectCollision.collisionBox.mainController.transform.position - containingAtom.mainController.transform.position));
                }
            });

            PostSave();

            SuperController.singleton.onBeforeSceneSaveHandlers += PreSave;
            SuperController.singleton.onSceneSavedHandlers += PostSave;

            StartCoroutine(LoadAssetsAfterSceneCompleted());
        }
        private IEnumerator LoadAssetsAfterSceneCompleted() {
            while (SuperController.singleton.isLoading) yield return new WaitForSeconds(0.5f);

            this._lid = FindElement(containingAtom.transform, "GunCase_Lid");
            if (this._lid == null) throw new InvalidOperationException("Invalid case");

            OnLidChange();
        }

        protected void OnDestroy() {
            if (this._lidRotator != null) this._lidRotator.onGrabEndHandlers -= this._releaseEvent;
            foreach (FreeControllerV3 entry in this._storedAtoms.Keys) entry.onGrabStartHandlers -= this._onGrabEvent;
            
            PreSave();

            SuperController.singleton.onBeforeSceneSaveHandlers -= PreSave;
            SuperController.singleton.onSceneSavedHandlers -= PostSave;
        }

        // destroy colliders
        protected void PreSave() {
            this._injectionCollisionBox?.collisionBox?.Remove();
            this._injectionCollisionBox = null;

            foreach (var entry in this._loadCollisionBox) entry.collisionBox.Remove();
            this._loadCollisionBox.Clear();

            /*this._lidRotator?.Remove();
            this._lidRotator = null;*/
        }

        // load colliders
        protected void PostSave() {
            StartCoroutine(GetCollisionBox(COLLISION_BOX_NAME.Replace("{object_id}", containingAtom.uid).Replace("{number}", "0")));
            for (int n = 0; n < LOAD_COLLISION_BOX_OFFSETS.Length; n++) StartCoroutine(GetLoadCollisionBox(COLLISION_BOX_NAME.Replace("{object_id}", containingAtom.uid).Replace("{number}", (n+1).ToString())));
            StartCoroutine(GetLidRotator(LID_ROTATOR_NAME.Replace("{object_id}", containingAtom.uid))); // TODO onAtomUIDsChangedHandlers change name?
        }

        public void OnCollision(Atom a) {
            // TODO see if load and auto store it
            this._storedAtoms.Add(a.mainController, PLACED_INJECTOR_OFFSET);
            a.mainController.onGrabStartHandlers += this._onGrabEvent;
        }

        public void OnLoadCollision(Atom a, Vector3 colliderPosition) {
            this._storedAtoms.Add(a.mainController, colliderPosition);
            a.mainController.onGrabStartHandlers += this._onGrabEvent;
        }

        public void FixedUpdate() {
            if (this._injectionCollisionBox?.collisionBox != null) {
                this._injectionCollisionBox.collisionBox.mainController.transform.position = containingAtom.mainController.transform.position + containingAtom.mainController.transform.rotation * COLLISION_BOX_OFFSET;
            }

            for (int n = 0; n < LOAD_COLLISION_BOX_OFFSETS.Length && n < this._loadCollisionBox.Count; n++) {
                this._loadCollisionBox[n].collisionBox.mainController.transform.position = containingAtom.mainController.transform.position + containingAtom.mainController.transform.rotation * LOAD_COLLISION_BOX_OFFSETS[n];
            }

            foreach (KeyValuePair<FreeControllerV3, Vector3> entry in this._storedAtoms) {
                // TODO the objects should have the same rotation that the box
                entry.Key.transform.position = containingAtom.mainController.transform.position + containingAtom.mainController.transform.rotation * entry.Value;
                entry.Key.transform.rotation = containingAtom.mainController.transform.rotation * ((entry.Value == PLACED_INJECTOR_OFFSET) ?
                            Quaternion.Euler(180f,-90f,90f) // injection
                            : Quaternion.Euler(-90f,90f,0f)); // load
            }

            if (this._lidRotator != null && this._lid != null) {
                this._lidRotator.transform.position = containingAtom.mainController.transform.position + containingAtom.mainController.transform.rotation * LID_OFFSET 
                        + this._lid.rotation * new Vector3(0, 0, LID_LENGHT);
            }
        }

        private void SetLidRotation(Quaternion r) {
            if (this._lid == null) return;
            this._lid.rotation = containingAtom.mainController.transform.rotation * r;
        }

        protected void OnLidChange() {
            // get angle difference between the lid vector and the final position; for simplicity we'll "rotate" the box and take the yz-plane
            Vector3 lidVector = new Vector3(0, 0, LID_LENGHT),
                    lidBasePosition = containingAtom.mainController.transform.position + containingAtom.mainController.transform.rotation * LID_OFFSET,
                    displacamentVector = Quaternion.Inverse(containingAtom.mainController.transform.rotation) * (this._lidRotator.transform.position - lidBasePosition),
                    displacamentVectorOnPlane = new Vector3(0f, displacamentVector.y, displacamentVector.z);
            float div = lidVector.magnitude * displacamentVectorOnPlane.magnitude;
            if (div == 0f) return;
            float angle = Mathf.Acos(Vector3.Dot(lidVector, displacamentVectorOnPlane) / div) * (180 / Mathf.PI);
            if (displacamentVectorOnPlane.y < 0) angle *= -1;

            //SuperController.LogMessage(angle.ToString());

            if (angle < 0f) {
                if (angle >= -90f) angle = 0;
                else angle = LID_MAX_ROTATION;
            }
            else {
                if (angle > LID_MAX_ROTATION) angle = LID_MAX_ROTATION;
            }
            SetLidRotation(Quaternion.Euler(-angle, 0, 0));
        }

        public static List<Atom> getCollidingAtoms(CollisionTriggerEventHandler handler) {
            List<Atom> r = new List<Atom>();
            foreach (KeyValuePair<Collider,bool> collider in handler.collidingWithDictionary) {
                Atom collided = SuperController.singleton.GetAtoms().FirstOrDefault(a =>
                        a.GetComponentsInChildren<Collider>().FirstOrDefault(c => c == collider.Key) != null);
                if (collided != null) r.Add(collided);
            }
            return r;
        }

        public static List<Atom> getCollidingAtoms(MorphInjectionCase.CollisionBox handler) {
            return getCollidingAtoms(handler.handler);
        }

        private Transform FindElement(Transform father, string searchName) {
            for (int n = 0; n < father.GetChildCount(); n++) {
                Transform child = father.GetChild(n);
                if (child.name == searchName) return child;
                Transform r = FindElement(child, searchName);
                if (r != null) return r;
            }

            return null;
        }
        
        private IEnumerator GetCollisionBox(string name) {
            // does it already exists?
            this._injectionCollisionBox = new MorphInjectionCase.CollisionBox(SuperController.singleton.GetAtoms().FirstOrDefault(a => a.name == name));
            if (this._injectionCollisionBox.collisionBox == null) {
                // no collision box; generate a new one
                yield return SuperController.singleton.AddAtomByType("CollisionTrigger", name);
                this._injectionCollisionBox = new MorphInjectionCase.CollisionBox(SuperController.singleton.GetAtoms().FirstOrDefault(a => a.name == name));

                // modify the new collision box
                this._injectionCollisionBox.collisionBox.GetStorableByID("scale").GetFloatJSONParam("scale").val = COLLISION_BOX_SIZE;

                JSONStorable trigger = this._injectionCollisionBox.collisionBox.GetStorableByID("Trigger");
                JSONClass triggerJSON = trigger.GetJSON();

                if (triggerJSON["trigger"]["startActions"].AsArray.Count == 0) {
                    triggerJSON["trigger"]["startActions"][0].Add("receiverAtom", "");
                    triggerJSON["trigger"]["startActions"][0].Add("receiver", "");
                    triggerJSON["trigger"]["startActions"][0].Add("receiverTargetName", "");
                    triggerJSON["trigger"]["startActions"][0].Add("floatValue", "");
                }
                triggerJSON["trigger"]["startActions"][0]["receiverAtom"].Value = containingAtom.name;
                triggerJSON["trigger"]["startActions"][0]["receiver"].Value = this.storeId;
                triggerJSON["trigger"]["startActions"][0]["receiverTargetName"].Value = "collision";
                triggerJSON["trigger"]["startActions"][0]["floatValue"].Value = "0";

                trigger.LateRestoreFromJSON(triggerJSON);
            }

            this._injectionCollisionBox.collisionBox.hidden = true;
        }

        
        
        private IEnumerator GetLoadCollisionBox(string name) {
            // does it already exists?
            MorphInjectionCase.CollisionBox cb = new MorphInjectionCase.CollisionBox(SuperController.singleton.GetAtoms().FirstOrDefault(a => a.name == name));
            if (cb.collisionBox == null) {
                // no collision box; generate a new one
                yield return SuperController.singleton.AddAtomByType("CollisionTrigger", name);
                cb = new MorphInjectionCase.CollisionBox(SuperController.singleton.GetAtoms().FirstOrDefault(a => a.name == name));

                // modify the new collision box
                cb.collisionBox.GetStorableByID("scale").GetFloatJSONParam("scale").val = COLLISION_BOX_SIZE;

                JSONStorable trigger = cb.collisionBox.GetStorableByID("Trigger");
                JSONClass triggerJSON = trigger.GetJSON();

                if (triggerJSON["trigger"]["startActions"].AsArray.Count == 0) {
                    triggerJSON["trigger"]["startActions"][0].Add("receiverAtom", "");
                    triggerJSON["trigger"]["startActions"][0].Add("receiver", "");
                    triggerJSON["trigger"]["startActions"][0].Add("receiverTargetName", "");
                    triggerJSON["trigger"]["startActions"][0].Add("floatValue", "");
                }
                triggerJSON["trigger"]["startActions"][0]["receiverAtom"].Value = containingAtom.name;
                triggerJSON["trigger"]["startActions"][0]["receiver"].Value = this.storeId;
                triggerJSON["trigger"]["startActions"][0]["receiverTargetName"].Value = "collision";
                triggerJSON["trigger"]["startActions"][0]["floatValue"].Value = (this._loadCollisionBox.Count + 1).ToString();

                trigger.LateRestoreFromJSON(triggerJSON);
            }

            this._loadCollisionBox.Add(cb);
            cb.collisionBox.hidden = true;
        }
        
        private IEnumerator GetLidRotator(string name) {
            // does it already exists?
            Atom rotator = SuperController.singleton.GetAtoms().FirstOrDefault(a => a.name == name);
            if (rotator == null) {
                // no atom; generate a new one
                yield return SuperController.singleton.AddAtomByType("CustomUnityAsset", name); // Empty atoms don't work on VR
                rotator = SuperController.singleton.GetAtoms().FirstOrDefault(a => a.name == name);
            }

            this._lidRotator = rotator.mainController;
            this._lidRotator.onGrabEndHandlers += this._releaseEvent;
            OnLidChange();
        }

        /*private IEnumerator GetLoadCollisionBox(string name) {
            // does it already exists?
            this._loadCollisionBox = SuperController.singleton.GetAtoms().FirstOrDefault(a => a.name == name);
            if (this._loadCollisionBox == null) {
                // no collision box; generate a new one
                SuperController.LogMessage("Generating collision box...");

                yield return SuperController.singleton.AddAtomByType("CollisionTrigger", name);
                this._loadCollisionBox = SuperController.singleton.GetAtoms().FirstOrDefault(a => a.name == name);

                // modify the new collision box
                this._loadCollisionBox.GetStorableByID("scale").GetFloatJSONParam("scale").val = LOAD_COLLISION_BOX_SIZE;

                JSONStorable trigger = this._loadCollisionBox.GetStorableByID("Trigger");
                JSONClass triggerJSON = trigger.GetJSON();

                if (triggerJSON["trigger"]["startActions"].AsArray.Count == 0) {
                    triggerJSON["trigger"]["startActions"][0].Add("receiverAtom", "");
                    triggerJSON["trigger"]["startActions"][0].Add("receiver", "");
                    triggerJSON["trigger"]["startActions"][0].Add("receiverTargetName", "");
                    triggerJSON["trigger"]["startActions"][0].Add("boolValue", "");
                }
                triggerJSON["trigger"]["startActions"][0]["receiverAtom"].Value = containingAtom.name;
                triggerJSON["trigger"]["startActions"][0]["receiver"].Value = this.storeId;
                triggerJSON["trigger"]["startActions"][0]["receiverTargetName"].Value = "attach";
                triggerJSON["trigger"]["startActions"][0]["boolValue"].Value = "true";

                trigger.LateRestoreFromJSON(triggerJSON);
            }
            this._loadCollisionBoxHandler = this._loadCollisionBox.GetComponentInChildren<CollisionTriggerEventHandler>();
        }*/

        public class CollisionBox {
            private Atom _collisionBox;
            private CollisionTriggerEventHandler _handler;

            public CollisionBox(Atom collisionBox) {
                this.collisionBox = collisionBox;
            }

            public Atom collisionBox {
                get { return this._collisionBox; }
                set {
                    this._collisionBox = value;
                    if (value != null) this._handler = value.GetComponentInChildren<CollisionTriggerEventHandler>();
                }
            }

            public CollisionTriggerEventHandler handler { get { return this._handler; } }
        }
    }
}