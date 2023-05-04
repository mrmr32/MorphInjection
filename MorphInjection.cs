using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SimpleJSON; // JSONNode
using MacGruber;

namespace JustAnotherUser {
    public class MorphInjection : MVRScript {
        private SimplifiedMorphInjectionLoad _load;

        private float _finalTime = 0f, // play the animation until finalTime
                    _duration = 0f;
        private IDictionary<DAZMorph, float> _queuedValues;

        private MorphInjection.CollisionBox _collisionBox, // area where to detect players
                             _loadCollisionBox; // area where to detect loads
        private FreeControllerV3.OnGrabStart _onGrabEvent;

        private JSONStorableBool _activeStorable, _attachStorable;

        private string _collisionBoxName, _loadCollisionBoxName;

        private static readonly string COLLISION_BOX_NAME = "MI_collision_{object_id}",
                                        LOAD_COLLISION_BOX_NAME = "MI_collision_{object_id}_load";
        private static readonly float COLLISION_BOX_SIZE = 0.02f,
                                    LOAD_COLLISION_BOX_SIZE = 0.1f;
        private static readonly Vector3 COLLISION_BOX_OFFSET = new Vector3(0f, 0.048f, -0.12f),
                                        LOAD_COLLISION_BOX_OFFSET = new Vector3(0f, 0.09f, -0.04f);

        // TODO sound atom

        private static readonly string VERSION = "2.3";

        public override void Init() {
            // plugin VaM GUI description
            pluginLabelJSON.val = "MorphInjection v" + VERSION;
        }


        // Runs once when plugin loads (after Init)
        protected void Start() {
            this._queuedValues = new Dictionary<DAZMorph, float>();

            this._activeStorable = new JSONStorableBool("run", false, (val) => {
                if (!val) return;

                this._activeStorable.val = false;
                Atom collided = getCollidingAtoms(this._collisionBox?.handler).FirstOrDefault(a => (a.type == "Person" && a.GetComponentInChildren<DAZCharacterSelector>() != null));
                if (collided == null) return;
                OnCollision(collided.GetComponentInChildren<DAZCharacterSelector>());
            });
            RegisterBool(this._activeStorable); // we don't care about this information, but we need to register it to set it on the trigger

            this._attachStorable = new JSONStorableBool("attach", false, (val) => {
                if (!val) return;

                this._attachStorable.val = false;
                Atom collided = getCollidingAtoms(this._loadCollisionBox?.handler).FirstOrDefault(a => a.GetComponentsInChildren<MVRScript>().Select(script => script.GetType().FullName).Any(scriptName => scriptName == "JustAnotherUser.MorphInjectionLoad"));
                if (collided == null) return;
                OnAttachCollision(new SimplifiedMorphInjectionLoad(collided, collided.GetComponentsInChildren<MVRScript>().FirstOrDefault(script => script.GetType().FullName == "JustAnotherUser.MorphInjectionLoad")));
            });
            RegisterBool(this._attachStorable); // we don't care about this information, but we need to register it to set it on the trigger

            this._onGrabEvent = (controller) => {
                // detach from injection
                if (this._load != null) {
                    this._load.atom.mainController.onGrabStartHandlers -= this._onGrabEvent; // remove previous event
                    this._load = null;
                }
            };

            this._collisionBoxName = COLLISION_BOX_NAME.Replace("{object_id}", containingAtom.uid);
            this._loadCollisionBoxName = LOAD_COLLISION_BOX_NAME.Replace("{object_id}", containingAtom.uid);

            PostSave();

            SuperController.singleton.onBeforeSceneSaveHandlers += PreSave;
            SuperController.singleton.onSceneSavedHandlers += PostSave;
        }

        protected void OnDestroy() {
            PreSave();

            if (this._load != null) this._load.atom.mainController.onGrabStartHandlers -= this._onGrabEvent;
            SuperController.singleton.onBeforeSceneSaveHandlers -= PreSave;
            SuperController.singleton.onSceneSavedHandlers -= PostSave;
        }

        // destroy colliders
        protected void PreSave() {
            this._collisionBox?.collisionBox?.Remove();
            this._loadCollisionBox?.collisionBox?.Remove();

            this._collisionBox = null;
            this._loadCollisionBox = null;
        }

        // load colliders
        protected void PostSave() {
            StartCoroutine(GetCollisionBox(this._collisionBoxName));
            StartCoroutine(GetLoadCollisionBox(this._loadCollisionBoxName));
        }

        public void OnCollision(DAZCharacterSelector person) {
            if (this._load == null || this._load.isEmpty) return;

            this._load.UseLoad();
            this._duration += this._load.duration;
            this._finalTime = (this._finalTime < Time.fixedTime) ? (Time.fixedTime + this._load.duration) : (this._finalTime + this._load.duration); // TODO not fixedTime
            foreach (DAZMorph morph in this._load.getAffectedMorphs(person)) {
                this._queuedValues.Add(morph, this._load.getMorphDifference(morph)); // TODO stack
            }
        }

        protected void OnAttachCollision(SimplifiedMorphInjectionLoad load) {
            if (this._load != null) this._load.atom.mainController.onGrabStartHandlers -= this._onGrabEvent; // remove previous event

            load.atom.mainController.onGrabStartHandlers += this._onGrabEvent;
            this._load = load;
        }

        public void FixedUpdate() {
            if (this._load != null) {
                // teleport load with the injection
                this._load.atom.mainController.transform.position = containingAtom.mainController.transform.position + containingAtom.mainController.transform.rotation
                    * (LOAD_COLLISION_BOX_OFFSET + new Vector3(0f,0.01f,0f));
                this._load.atom.mainController.transform.rotation = containingAtom.mainController.transform.rotation;
            }

            if (this._collisionBox?.collisionBox != null) {
                // teleport the collider with the injection
                this._collisionBox.collisionBox.mainController.transform.position = containingAtom.mainController.transform.position + containingAtom.mainController.transform.rotation * COLLISION_BOX_OFFSET;
            }

            if (this._loadCollisionBox?.collisionBox != null) {
                // teleport the load collider with the injection
                this._loadCollisionBox.collisionBox.mainController.transform.position = containingAtom.mainController.transform.position + containingAtom.mainController.transform.rotation * LOAD_COLLISION_BOX_OFFSET;
            }
        }

        public void Update() {
            if (this._finalTime < Time.fixedTime) {
                this._duration = 0f;
                this._queuedValues.Clear();
                return;
            }

            // play animation
            float secondsSinceLastUpdate = Time.deltaTime;
            foreach (KeyValuePair<DAZMorph,float> entry in this._queuedValues) {
                float addPerAnimation = entry.Value,
                    addPerSecond = addPerAnimation / this._duration;
                entry.Key.SetValue(entry.Key.appliedValue + addPerSecond*secondsSinceLastUpdate);
                entry.Key.SyncJSON();
            }
            // TODO check GetFloatJSONParamMaxValue (?)
        }

        public static List<Atom> getCollidingAtoms(CollisionTriggerEventHandler handler) {
            List<Atom> r = new List<Atom>();
            if (handler == null) return r;

            foreach (KeyValuePair<Collider,bool> collider in handler.collidingWithDictionary) {
                Atom collided = SuperController.singleton.GetAtoms().FirstOrDefault(a =>
                        a.GetComponentsInChildren<Collider>().FirstOrDefault(c => c == collider.Key) != null);
                if (collided != null) r.Add(collided);
            }
            return r;
        }

        private IEnumerator GetCollisionBox(string name) {
            // does it already exists?
            this._collisionBox = new MorphInjection.CollisionBox(SuperController.singleton.GetAtoms().FirstOrDefault(a => a.name == name));
            if (this._collisionBox.collisionBox == null) {
                // no collision box; generate a new one
                yield return SuperController.singleton.AddAtomByType("CollisionTrigger", name);
                this._collisionBox = new MorphInjection.CollisionBox(SuperController.singleton.GetAtoms().FirstOrDefault(a => a.name == name));

                // modify the new collision box
                this._collisionBox.collisionBox.GetStorableByID("scale").GetFloatJSONParam("scale").val = COLLISION_BOX_SIZE;

                JSONStorable trigger = this._collisionBox.collisionBox.GetStorableByID("Trigger");
                JSONClass triggerJSON = trigger.GetJSON();

                if (triggerJSON["trigger"]["startActions"].AsArray.Count == 0) {
                    triggerJSON["trigger"]["startActions"][0].Add("receiverAtom", "");
                    triggerJSON["trigger"]["startActions"][0].Add("receiver", "");
                    triggerJSON["trigger"]["startActions"][0].Add("receiverTargetName", "");
                    triggerJSON["trigger"]["startActions"][0].Add("boolValue", "");
                }
                triggerJSON["trigger"]["startActions"][0]["receiverAtom"].Value = containingAtom.name;
                triggerJSON["trigger"]["startActions"][0]["receiver"].Value = this.storeId;
                triggerJSON["trigger"]["startActions"][0]["receiverTargetName"].Value = "run";
                triggerJSON["trigger"]["startActions"][0]["boolValue"].Value = "true";

                trigger.LateRestoreFromJSON(triggerJSON);
            }

            this._collisionBox.collisionBox.hidden = true;
        }

        private IEnumerator GetLoadCollisionBox(string name) {
            // does it already exists?
            this._loadCollisionBox = new MorphInjection.CollisionBox(SuperController.singleton.GetAtoms().FirstOrDefault(a => a.name == name));
            if (this._loadCollisionBox.collisionBox == null) {
                // no collision box; generate a new one
                yield return SuperController.singleton.AddAtomByType("CollisionTrigger", name);
                this._loadCollisionBox = new MorphInjection.CollisionBox(SuperController.singleton.GetAtoms().FirstOrDefault(a => a.name == name));

                // modify the new collision box
                this._loadCollisionBox.collisionBox.GetStorableByID("scale").GetFloatJSONParam("scale").val = LOAD_COLLISION_BOX_SIZE;

                JSONStorable trigger = this._loadCollisionBox.collisionBox.GetStorableByID("Trigger");
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

            this._loadCollisionBox.collisionBox.hidden = true;
        }

        
        protected class SimplifiedMorphInjectionLoad {
            private JSONStorable _morphInjectionLoadScript;

            public Atom atom;
            private JSONStorableBool _emptyStorable;
            private EventTrigger _events;
            private bool _unlimited;
            public float duration;
            public bool isEmpty { get { return this._emptyStorable.val; } }

            private IDictionary<string, float> _morphIncrement, // all the morphs that it should modify and its increment
                                               _morphSet;       // all the morphs that it should modify and its final value

            public SimplifiedMorphInjectionLoad(Atom atom, MVRScript morphInjectionLoadScript) {
                this._morphIncrement = new Dictionary<string, float>();
                this._morphSet = new Dictionary<string, float>();

                this._morphInjectionLoadScript = morphInjectionLoadScript;
                this.atom = atom;

                this._events = new EventTrigger(morphInjectionLoadScript, "OnCollide");
                this._events.RestoreFromJSON(morphInjectionLoadScript.GetJSON(), morphInjectionLoadScript.subScenePrefix,
                    morphInjectionLoadScript.mergeRestore, true);

                // we want to get emptyStorable, if it's unlimited, the duration and the morphs (increment/set)
                foreach (string paramName in morphInjectionLoadScript.GetAllParamAndActionNames()) {
                    try {
                        JSONStorableParam param = morphInjectionLoadScript.GetParam(paramName);
                        if (param == null) continue;

                        switch (param.name) {
                            case "empty":
                                this._emptyStorable = (JSONStorableBool)param;
                                break;

                            case "unlimited":
                                this._unlimited = ((JSONStorableBool)param).val;
                                break;

                            case "duration":
                                this.duration = ((JSONStorableFloat)param).val;
                                break;

                            default:
                                bool increment = param.name.StartsWith("Inc#");
                                if (!increment && !param.name.StartsWith("Set#")) break; // I don't know what this property is

                                IDictionary<string, float> destiny = (increment ? this._morphIncrement : this._morphSet);
                                destiny.Add(param.name.Remove(0, 4), ((JSONStorableFloat)param).val);
                                break;
                        }
                    } catch (Exception ex) { }
                }
            }

            // set as empty, unless it's unlimited
            public void UseLoad() {
                if (!this._unlimited) this._emptyStorable.val = true;
                this._events.Trigger();
            }

            public List<DAZMorph> getAffectedMorphs(DAZCharacterSelector character) {
                List<DAZMorph> r = new List<DAZMorph>();
                List<DAZMorph> morphs = new List<DAZMorph>();
                ScanBank(character.morphBank1, morphs);
                ScanBank(character.morphBank2, morphs);
                ScanBank(character.morphBank3, morphs);

                // add increment morphs
                foreach (string morph in this._morphIncrement.Keys) {
                    DAZMorph val = FindMorphByName(morphs, morph);
                    if (val != null) r.Add(val);
                }

                // add set morphs
                foreach (string morph in this._morphSet.Keys) {
                    DAZMorph val = FindMorphByName(morphs, morph);
                    if (val != null) r.Add(val);
                }

                return r;
            }

            // final morph value after 1 animation ends
            public float getMorphDifference(DAZMorph morph) {
                if (this._morphSet.Keys.Contains(morph.displayName)) {
                    // set
                    float final = 0f;
                    try {
                        final = this._morphSet[morph.displayName];
                    } catch (Exception ex) { /* maybe in any list? */ }
                    return final - morph.appliedValue;
                }
                else {
                    // increment
                    return this._morphIncrement[morph.displayName];
                }
            }

            private void ScanBank(DAZMorphBank bank, List<DAZMorph> morphs) { // TODO only morph (not morph & pose)
                if (bank == null) return;

                foreach (DAZMorph morph in bank.morphs) {
                    if (!morph.visible) continue;

                    morphs.Add(morph);
                }
            }

            private DAZMorph FindMorphByName(List<DAZMorph> morphs, string name) {
                foreach (DAZMorph morph in morphs) {
                    if (!morph.displayName.Equals(name)) continue;

                    return morph;
                }

                return null; // not found
            }
        }

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