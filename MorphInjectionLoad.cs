using System;
using System.Collections;
using System.Collections.Generic;
using MVR.FileManagementSecure;
using System.Linq;
using UnityEngine;
using SimpleJSON;
using System.Text.RegularExpressions;
using MacGruber;

namespace JustAnotherUser {
    // contains the information that the injection will perform
    public class MorphInjectionLoad : MVRScript {
        private IDictionary<DAZCharacterSelector, List<DAZMorph>> _morphs;

        private JSONStorableBool _emptyStorable,
                                _unlimitedStorable;
        private JSONStorableFloat _durationStorable;
        private JSONStorableColor _colorStorable;
        private JSONStorableUrl _imageStorable;
        // TODO atom filter
        public EventTrigger _events;

        private JSONStorableStringChooser _morphChooseListIncrement,
                                        _morphChooseListSet;
        private Texture _defaultTexture;
        private Material _liquidMaterial;
        private int _steps = 0;

        private FreeControllerV3.OnGrabStart _loadGrabbed;
        private FreeControllerV3.OnGrabEnd _loadReleased;
        private SuperController.OnSceneLoaded _beforeSave,
                                                _afterSave;

        private MenuHelper _gui;
        private Menu _currentMenu;

        private IDictionary<string, JSONStorableFloat> _morphIncrement, // all the morphs that it should modify and its increment
                                                       _morphSet;       // all the morphs that it should modify and its final value

        private static readonly string VERSION = "2.3";
        private static readonly float SECONDS_ANIMATION = 3f;
        private static readonly Color STEP_ANIMATION = new Color(-1f / SECONDS_ANIMATION, -1f / SECONDS_ANIMATION, -1f / SECONDS_ANIMATION, 0f);
        private static readonly float MAX_RAYCAST_HELP = 5f;

        public bool isEmpty { get { return this._emptyStorable.val; } }

        public float duration { get { return this._durationStorable.val; } }

        public IEnumerable<string> morphNames {
            get {
                HashSet<string> r = new HashSet<string>();
                foreach (List<DAZMorph> e in this._morphs.Values) {
                    foreach (DAZMorph morph in e) r.Add(morph.displayName);
                }
                return r;
            }
        }

        public override void Init() {
            // plugin VaM GUI description
            pluginLabelJSON.val = "MorphInjection [Load] v" + VERSION;

            this._morphs = new Dictionary<DAZCharacterSelector, List<DAZMorph>>();
            this._morphIncrement = new Dictionary<string, JSONStorableFloat>();
            this._morphSet = new Dictionary<string, JSONStorableFloat>();
            // GUI
            this._gui = new MenuHelper(this);

            this._unlimitedStorable = new JSONStorableBool("unlimited", false, (bool val) => {
                if (val && this._emptyStorable.val == true) {
                    this._emptyStorable.val = false;
                    SetColor(this._colorStorable.colorPicker.currentColor);
                }
            });
            RegisterBool(this._unlimitedStorable);

            this._emptyStorable = new JSONStorableBool("empty", false, (bool val) => {
                if (!val) SetColor(this._colorStorable.colorPicker.currentColor);
            });
            RegisterBool(this._emptyStorable);

            this._durationStorable = new JSONStorableFloat("duration", 10.0f, 0.1f, 120.0f);
            RegisterFloat(this._durationStorable);

            this._colorStorable = new JSONStorableColor("color", new HSVColor { H = 0.82f, S = 1f, V = 1f }, (color) => SetColor(color.colorPicker.currentColor));
            RegisterColor(this._colorStorable);

            // image GUI
            this._imageStorable = new JSONStorableUrl("image", string.Empty, (JSONStorableString.SetStringCallback)(path => SetImage(path)), "png|jpg|jpeg|tiff|tif|bmp", "Custom/Images");
            RegisterUrl(this._imageStorable);

            // morphs GUI
            this._morphChooseListIncrement = new JSONStorableStringChooser("increment", this.morphNames.ToList(), "", "Add morph to increment", (string morphName) => {
                if (this._morphIncrement.Keys.Contains(morphName)) {
                    SuperController.LogMessage(morphName + " already added");
                    return;
                }

                // TODO remove from the other list
                SuperController.LogMessage("Added " + morphName);
                AddMorph(morphName, true);
            });
            // we don't care about this information

            this._morphChooseListSet = new JSONStorableStringChooser("set", this._morphChooseListIncrement.choices, "", "Add morph to set", (string morphName) => {
                if (this._morphSet.Keys.Contains(morphName)) {
                    SuperController.LogError(morphName + " already added");
                    return;
                }

                // TODO remove from the other list
                SuperController.LogMessage("Added " + morphName);
                AddMorph(morphName, false);
            });
            // we don't care about this information

            this._events = new EventTrigger(this, "OnCollide");
        }

        // Runs once when plugin loads (after Init)
        protected void Start() {
            // enable Morphs listeners
            this._beforeSave =  () => {
                // to save the morphSet (that can be 0; the default value) we need to make sure its value is not the default one
                foreach (JSONStorableFloat e in this._morphSet.Values) {
                    if (e.val == e.defaultVal) e.defaultVal = 0.01f;
                }
            };
            SuperController.singleton.onBeforeSceneSaveHandlers += this._beforeSave;

            this._afterSave = () => {
                // once is saved, we can return to normal
                foreach (JSONStorableFloat e in this._morphSet.Values) e.defaultVal = 0f;
            };
            SuperController.singleton.onSceneSavedHandlers += this._afterSave;

            createUI();

            // load
            this._loadGrabbed = (load) => {
                if (this == null) return; // TODO this shouldn't happend (we unregister it)

                Grabber grabber;
                if (SuperController.singleton.GetLeftGrab()) grabber = Grabber.L_HAND;
                else if (SuperController.singleton.GetRightGrab()) grabber = Grabber.R_HAND;
                else grabber = Grabber.MOUSE; // it can also be remote grab

                if (this._loadReleased != null) {
                    // grabbed without being released before?
                    this._loadReleased(containingAtom.mainController); // force invoke (it will get removed inside the function)
                }

                this._loadReleased = (l) => {
                    if (this == null) return; // TODO this shouldn't happend (we unregister it)

                    OnAtomReleased(grabber);
                    containingAtom.mainController.onGrabEndHandlers -= this._loadReleased;
                    this._loadReleased = null;
                };
                containingAtom.mainController.onGrabEndHandlers += this._loadReleased;
            };

            this._loadReleased = null;
            containingAtom.mainController.onGrabStartHandlers += this._loadGrabbed;

            StartCoroutine(LoadTexturesAfterSceneCompleted());
            SimpleTriggerHandler.LoadAssets();
        }

        protected void createUI() {
            this._currentMenu = Menu.MAIN;
            this._gui.ClearUI();

            this._gui.CreateToggle(this._unlimitedStorable);
            this._gui.CreateToggle(this._emptyStorable);

            this._gui.CreateSlider(this._durationStorable, true); // create on the right

            this._gui.CreateColorPicker(this._colorStorable);

            var imageName = new JSONStorableString("imageName", "");
            imageName.disableOnEndEdit = true;
            var imageNameUI = this._gui.CreateTextField(imageName, true);
            this._imageStorable.text = imageNameUI.UItext;
            var browseButton = this._gui.CreateButton("pick image", true);
            this._imageStorable.fileBrowseButton = browseButton.button;
            var clearButton = this._gui.CreateButton("reset image", true);
            this._imageStorable.clearButton = clearButton.button;
            this._imageStorable.clearButton.onClick.AddListener(() => {
                Renderer label = FindElement(containingAtom.transform, "label")?.GetComponent<Renderer>();
                if (label == null || this._defaultTexture == null) return;

                Graphics.CopyTexture(this._defaultTexture, label.material.mainTexture); // TODO why not working?
            });

            // TODO at the end
            var morphsButton = this._gui.CreateButton("Go to morphs ->");
            morphsButton.button.onClick.AddListener(createMorphsUI);

            var triggersButton = this._gui.CreateButton("Go to triggers ->");
            triggersButton.button.onClick.AddListener(this._events.OpenPanel);
        }

        protected void createMorphsUI() {
            this._currentMenu = Menu.MORPHS;
            this._gui.ClearUI();

            var backButton = this._gui.CreateButton("<- Go back");
            backButton.button.onClick.AddListener(createUI);

            ResyncMorphs();

            var linkPopup = this._gui.CreateFilterablePopup(this._morphChooseListIncrement);
            linkPopup.popupPanelHeight = 600f;

            linkPopup = this._gui.CreateFilterablePopup(this._morphChooseListSet, true);
            linkPopup.popupPanelHeight = 600f;

            foreach (JSONStorableFloat storables in this._morphIncrement.Values) this._gui.CreateSlider(storables, false);
            foreach (JSONStorableFloat storables in this._morphSet.Values) this._gui.CreateSlider(storables, true);
            // TODO add remove button
        }

        protected void createTriggersUI() {
            this._currentMenu = Menu.TRIGGERS;
            this._gui.ClearUI();

            var backButton = this._gui.CreateButton("<- Go back");
            backButton.button.onClick.AddListener(createUI);
        }

        protected void Update() {
            this._events.Update();

            if (this._liquidMaterial == null) return;

            Color c;
            if (this._emptyStorable.val && (c = this._liquidMaterial.color) != Color.black) {
                c += STEP_ANIMATION * Time.deltaTime;
                if (c.r < 0f) c.r = 0f;
                if (c.g < 0f) c.g = 0f;
                if (c.b < 0f) c.b = 0f;

                SetColor(c, true);
            }
        }

        
		private void OnAtomRename(string oldid, string newid) {
            this._events.SyncAtomNames();
        }

        protected void OnDestroy() {
            containingAtom.mainController.onGrabStartHandlers -= this._loadGrabbed;
            if (this._loadReleased != null) containingAtom.mainController.onGrabEndHandlers -= this._loadReleased;
            //this._events.Remove();

            SuperController.singleton.onBeforeSceneSaveHandlers -= this._beforeSave;
            SuperController.singleton.onSceneSavedHandlers -= this._afterSave;

            this._events.Remove();
        }

        protected void OnAtomReleased(Grabber grabber) {
            if (grabber != Grabber.MOUSE) return;

            // PC assistant load attachment
            Vector3? origin = SuperController.singleton.centerCameraTarget?.transform?.position;
            if (origin == null) return;
            Ray raycast = new Ray((Vector3)origin, containingAtom.mainController.transform.position - (Vector3)origin);
            RaycastHit []hits = Physics.RaycastAll(raycast, MAX_RAYCAST_HELP);
            Array.Sort(hits, (a,b) => a.distance.CompareTo(b.distance));
            Atom injectionCollider = null;
            for (int n = 0; n < hits.Length; n++) {
                Atom atomHit = getAtomByRigidbody(hits[n]);

                if (atomHit.name == containingAtom.name) continue; // all OK; keep searching the collider

                if (atomHit.type == "CollisionTrigger") {
                    Match matcher = new Regex("^MI_collision_(.+)_load$").Match(atomHit.name);
                    if (matcher.Success) {
                        // collided with the load trigger
                        injectionCollider = atomHit;
                        break; // exit
                    }
                    else continue; // collided with the injection trigger; in the next iteration we'll get the injection itself
                }

                if (atomHit.GetComponentsInChildren<MVRScript>().Any(s => s.GetType().FullName == "JustAnotherUser.MorphInjection")) {
                    // collided with the injection; get the trigger
                    /*injectionCollider = SuperController.singleton.GetAtoms().FirstOrDefault(a => a.name == "MI_collision_" + atomHit.name + "_load");
                    break;*/ // exit

                    continue; // the ray is very unprecise
                }

                return; // no match => no direct view
            }

            if (injectionCollider == null) return;
            containingAtom.mainController.transform.position = injectionCollider.mainController.transform.position;
        }

        public static Atom getAtomByRigidbody(Rigidbody rigidbody) {
            return SuperController.singleton.GetAtoms().FirstOrDefault(a =>
                        a.rigidbodies.Any(r => r == rigidbody));
        }

        public static Atom getAtomByRigidbody(RaycastHit hit) {
            return getAtomByRigidbody(hit.rigidbody);
        }

        private IEnumerator LoadTexturesAfterSceneCompleted() {
            while (SuperController.singleton.isLoading) yield return new WaitForSeconds(0.5f);

            this._liquidMaterial = FindElement(containingAtom.transform, "indicator")?.GetComponent<Renderer>()?.material;

            SetColor(this.isEmpty ? Color.black : this._colorStorable.colorPicker.currentColor, true);
            SetImage(this._imageStorable.val);
        }

        protected void SetImage(string path) {
            Renderer label = FindElement(containingAtom.transform, "label")?.GetComponent<Renderer>();
            if (label == null) return;

            if (this._defaultTexture == null) {
                this._defaultTexture = new Texture2D(512, 512);
                Graphics.CopyTexture(label.material.mainTexture, this._defaultTexture);
            }

            Texture2D loadedImage = new Texture2D(512, 512);
            ImageConversion.LoadImage(loadedImage, FileManagerSecure.ReadAllBytes(path));
            label.material.mainTexture = loadedImage;
        }

        protected void SetColor(Color color, bool force = false) {
            if (isEmpty && !force) return;
            if (this._liquidMaterial == null) return; // not the right asset
            this._liquidMaterial.color = color;
            this._liquidMaterial.SetColor("_EmissionColor", color);
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

        // set as empty, unless it's unlimited
        public void UseLoad() {
            if (!this._unlimitedStorable.val) this._emptyStorable.val = true;
            // TODO update (latest on MorphInjection)
        }

        public List<DAZMorph> getAffectedMorphs(DAZCharacterSelector character) {
            List<DAZMorph> r = new List<DAZMorph>();

            // add increment morphs
            foreach (string morph in this._morphIncrement.Keys) {
                DAZMorph val = FindMorphByName(this._morphs[character], morph);
                if (val != null) r.Add(val);
            }

            // add set morphs
            foreach (string morph in this._morphSet.Keys) {
                DAZMorph val = FindMorphByName(this._morphs[character], morph);
                if (val != null) r.Add(val);
            }

            return r;
        }

        // final morph value after 1 animation ends
        public float getMorphFinalValue(DAZMorph morph) {
            if (this._morphSet.Keys.Contains(morph.displayName)) {
                // set
                return this._morphSet[morph.displayName].val;
            }
            else {
                // increment
                float increment = 0f;
                try {
                    increment = this._morphIncrement[morph.displayName].val;
                } catch (Exception ex) { /* maybe in any list? */ }
                return morph.appliedValue + increment;
            }
        }

        public void ResyncMorphs() {
            this._morphs.Clear();

            foreach (Atom atom in SuperController.singleton.GetAtoms()) {
                DAZCharacterSelector characterSelector;
                if (atom.type != "Person" || (characterSelector = atom.GetComponentInChildren<DAZCharacterSelector>()) == null) continue;

                List<DAZMorph> morphs = new List<DAZMorph>();
                ScanBank(characterSelector.morphBank1, morphs); // @author https://github.com/ProjectCanyon/morph-merger/blob/master/MorphMerger.cs
                ScanBank(characterSelector.morphBank2, morphs);
                ScanBank(characterSelector.morphBank3, morphs);
                this._morphs.Add(characterSelector, morphs);
            }

            if (this._morphChooseListIncrement != null) {
                this._morphChooseListIncrement.choices = this.morphNames.ToList();
                this._morphChooseListSet.choices = this._morphChooseListIncrement.choices;
            }
        }

        private void AddMorph(string morphName, bool increment = true, float value = 0.0f) {
            JSONStorableFloat jsonFloat = new JSONStorableFloat((increment ? "Inc#" : "Set#") + morphName, 0.0f, -1.0f, 1.0f);
            jsonFloat.val = value;
            RegisterFloat(jsonFloat);

            if (increment) this._morphIncrement.Add(morphName, jsonFloat);
            else this._morphSet.Add(morphName, jsonFloat);

            if (this._currentMenu == Menu.MORPHS) createMorphsUI(); // update the UI
        }

        
		public override JSONClass GetJSON(bool includePhysical = true, bool includeAppearance = true, bool forceStore = false) {
            JSONClass jc = base.GetJSON(includePhysical, includeAppearance, forceStore);
            if (includePhysical || forceStore) jc[this._events.Name] = this._events.GetJSON(base.subScenePrefix);
            return jc;
        }
        
		public override void LateRestoreFromJSON(JSONClass jc, bool restorePhysical = true, bool restoreAppearance = true, bool setMissingToDefault = true) {
            base.LateRestoreFromJSON(jc, restorePhysical, restoreAppearance, setMissingToDefault);

            if (!base.physicalLocked && restorePhysical) {
                this._events.Remove();

                this._events.RestoreFromJSON(jc, base.subScenePrefix, base.mergeRestore, setMissingToDefault);

                // TODO with late restore the data already loads?
                foreach (string entry in jc.Keys) {
                    switch (entry) {
                        case "duration":
                        case "unlimited":
                        case "empty":
                        case "color":
                        case "image":
                        case "id":
                        case "pluginLabel":
                            break; // ignore

                        default:
                            // tracked morphs
                            try {
                                bool increment = entry.StartsWith("Inc#");
                                if (!increment && !entry.StartsWith("Set#")) break; // I don't know what this property is

                                AddMorph(entry.Remove(0, 4), increment, jc[entry].AsFloat);
                            } catch (Exception ex) { }
                            break;
                    }
                }
            }
        }

        // @author https://raw.githubusercontent.com/ChrisTopherTa54321/VamScripts/master/FloatMultiParamRandomizer.cs
        public JSONNode GetPluginJsonFromSave() {
            foreach (JSONNode atoms in SuperController.singleton.loadJson["atoms"].AsArray) {
                if (!atoms["id"].Value.Equals(containingAtom.name)) continue;

                foreach (JSONNode storable in atoms["storables"].AsArray) {
                    if (storable["id"].Value == this.storeId) {
                        return storable;
                    }
                }
            }

            return null;
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

        public class MenuHelper {
            private MVRScript _gui;
            private IDictionary<object /* UIDynamicButton for buttons; JSONStorableXXXX for the rest */, UIElements> _storables;

            public MenuHelper(MVRScript script) {
                this._gui = script;
                this._storables = new Dictionary<object, UIElements>();
            }

            public UIDynamicButton CreateButton(string name, bool right = false) {
                UIDynamicButton btn = this._gui.CreateButton(name, right);
                this._storables.Add(btn, UIElements.BUTTON);
                return btn;
            }

            public UIDynamicPopup CreateFilterablePopup(JSONStorableStringChooser stringChooser, bool right = false) {
                this._storables.Add(stringChooser, UIElements.POPUP);
                return this._gui.CreateFilterablePopup(stringChooser, right);
            }

            public UIDynamicTextField CreateTextField(JSONStorableString str, bool right = false) {
                this._storables.Add(str, UIElements.TEXT);
                return this._gui.CreateTextField(str, right);
            }

            public UIDynamicSlider CreateSlider(JSONStorableFloat fl, bool right = false) {
                this._storables.Add(fl, UIElements.SLIDER);
                return this._gui.CreateSlider(fl, right);
            }

            public UIDynamicToggle CreateToggle(JSONStorableBool chooser, bool right = false) {
                this._storables.Add(chooser, UIElements.TOGGLER);
                return this._gui.CreateToggle(chooser, right);
            }

            public UIDynamicColorPicker CreateColorPicker(JSONStorableColor color, bool right = false) {
                this._storables.Add(color, UIElements.COLOR_PICKER);
                return this._gui.CreateColorPicker(color, right);
            }

            public void ClearUI() {
                foreach (KeyValuePair<object,UIElements> entry in this._storables) {
                    switch (entry.Value.value) {
                        case UIElements.BUTTON_VALUE:
                            this._gui.RemoveButton((UIDynamicButton)entry.Key);
                            break;

                        case UIElements.TEXT_VALUE:
                            this._gui.RemoveTextField((JSONStorableString)entry.Key);
                            break;

                        case UIElements.SLIDER_VALUE:
                            this._gui.RemoveSlider((JSONStorableFloat)entry.Key);
                            break;

                        case UIElements.POPUP_VALUE:
                            this._gui.RemovePopup((JSONStorableStringChooser)entry.Key);
                            break;

                        case UIElements.TOGGLER_VALUE:
                            this._gui.RemoveToggle((JSONStorableBool)entry.Key);
                            break;

                        case UIElements.COLOR_PICKER_VALUE:
                            this._gui.RemoveColorPicker((JSONStorableColor)entry.Key);
                            break;
                    }
                }

                this._storables.Clear();
            }

            protected class UIElements {
                public const int BUTTON_VALUE = 0,
                                TEXT_VALUE = 1,
                                SLIDER_VALUE = 2,
                                POPUP_VALUE = 3,
                                TOGGLER_VALUE = 4,
                                COLOR_PICKER_VALUE = 5;

                public static readonly UIElements BUTTON = new UIElements(BUTTON_VALUE),
                                            TEXT = new UIElements(TEXT_VALUE),
                                            SLIDER = new UIElements(SLIDER_VALUE),
                                            POPUP = new UIElements(POPUP_VALUE),
                                            TOGGLER = new UIElements(TOGGLER_VALUE),
                                            COLOR_PICKER = new UIElements(COLOR_PICKER_VALUE);

                private int _value;

                public int value { get { return this._value; } }

                private UIElements(int value) {
                    this._value = value;
                }

                public static bool operator ==(UIElements obj1, UIElements obj2) {
                    return obj1?._value == obj2?._value;
                }

                public static bool operator !=(UIElements obj1, UIElements obj2) {
                    return obj1?._value != obj2?._value;
                }
            }
        }

        // an enum will crash... why?
        protected class Grabber {
            public static readonly Grabber R_HAND = new Grabber(0),
                                        L_HAND = new Grabber(1),
                                        MOUSE = new Grabber(2);

            private int _value;
            private Grabber(int value) {
                this._value = value;
            }

            public static bool operator ==(Grabber obj1, Grabber obj2) {
                return obj1?._value == obj2?._value;
            }

            public static bool operator !=(Grabber obj1, Grabber obj2) {
                return obj1?._value != obj2?._value;
            }
        }
        
        protected class Menu {
            public static readonly Menu MAIN = new Menu(0),
                                        MORPHS = new Menu(1),
                                        TRIGGERS = new Menu(2);

            private int _value;
            private Menu(int value) {
                this._value = value;
            }

            public static bool operator ==(Menu obj1, Menu obj2) {
                return obj1?._value == obj2?._value;
            }

            public static bool operator !=(Menu obj1, Menu obj2) {
                return obj1?._value != obj2?._value;
            }
        }
    }
}