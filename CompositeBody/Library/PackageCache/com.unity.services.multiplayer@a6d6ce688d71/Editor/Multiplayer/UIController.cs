using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Services.Multiplayer.Editor
{
    #region Controller
    class UIController : UIEditorService
    {
        public readonly Color TitleBackground = new Color(0f, 0f, 0f, 0.2f);
        public readonly Color HeaderBackground = new Color(0f, 0f, 0f, 0.1f);
        public readonly Color PanelBackground = new Color(0.4f, 0.4f, 0.4f, 0.05f);
        public readonly Color FooterBackground = new Color(0f, 0f, 0f, 0.05f);
        public readonly Color PanelBorder = new Color(0.4f, 0.4f, 0.4f, 0.5f);

        public readonly Color ErrorBackground = new Color(0.5f, 0.1f, 0.1f, 0.3f);
        public readonly Color SuccessBackground = new Color(0f, 0.5f, 0.2f, 0.3f);
        public readonly Color InfoBackground = new Color(0.2f, 0.2f, 0.5f, 0.3f);
        public readonly Color WarningBackground = new Color(0.5f, 0.5f, 0.1f, 0.3f);
        public readonly Color HighlightBackground = new Color(0f, 0.3f, 0.3f, 0.3f);

        public readonly Color BlockBackground = new Color(0.5f, 0.5f, 0.5f, 0.1f);
        public readonly Color BlockHoverBackground = new Color(0.5f, 0.5f, 0.5f, 0.15f);
        public readonly Color BlockBorder = new Color(0.3f, 0.3f, 0.3f, 0.2f);

        public UIController()
        {
        }

        public UIButton Button(string text, Action action)
        {
            return Button()
                .SetText(text)
                .SetClickCallback(action);
        }

        public UIButton Button(string text, Func<Task> action)
        {
            return Button()
                .SetText(text)
                .SetClickCallback(action);
        }

        public override UILabel Label()
        {
            return base.Label()
                .SetName("Label")
                .SetWhitespace(WhiteSpace.Normal);
        }

        public UILabel Label(string text)
        {
            return Label()
                .SetText(text);
        }

        public override UISelectableLabel SelectableLabel()
        {
            return base.SelectableLabel()
                .SetName("SelectableLabel")
                .SetWhitespace(WhiteSpace.Normal);
        }

        public UISelectableLabel SelectableLabel(string text)
        {
            return SelectableLabel()
                .SetName("SelectableLabel")
                .SetText(text);
        }

        public UILabel H1(string text)
        {
            return Label()
                .SetName("H1")
                .SetText(text)
                .SetFontStyle(FontStyle.Bold)
                .SetFontSize(18);
        }

        public UILabel H2(string text)
        {
            return Label()
                .SetName("H2")
                .SetText(text)
                .SetFontStyle(FontStyle.Bold)
                .SetFontSize(17);
        }

        public UILabel H3(string text)
        {
            return Label()
                .SetName("H3")
                .SetText(text)
                .SetFontStyle(FontStyle.Bold)
                .SetFontSize(16);
        }

        public UILabel H4(string text)
        {
            return Label()
                .SetName("H4")
                .SetText(text)
                .SetFontStyle(FontStyle.Bold)
                .SetFontSize(15);
        }

        public UILabel H5(string text)
        {
            return Label()
                .SetName("H5")
                .SetText(text)
                .SetFontStyle(FontStyle.Bold)
                .SetFontSize(14);
        }

        public UIElement Separator(Color? color = null)
        {
            return Element()
                .SetName("Separator")
                .SetBorderTopWidth(2)
                .SetBorderTopColor(color.HasValue ? color.Value : new Color(0.5f, 0.5f, 0.5f, 1f))
                .SetMargin(5, 0, 5, 0);
        }

        public UIElement Space()
        {
            return Element()
                .SetName("Space")
                .SetPadding(5, 5, 5, 5);
        }

        public UIElement Space(StyleLength length)
        {
            return Element()
                .SetName("Space")
                .SetPadding(length, length, length, length);
        }

        public UIElement VerticalSpace(StyleLength length)
        {
            return Element()
                .SetName("VerticalSpace")
                .SetPadding(length, 0, length, 0);
        }

        public UIElement HorizontalSpace(StyleLength length)
        {
            return Element()
                .SetName("HorizontalSpace")
                .SetPadding(0, length, 0, length);
        }

        public UIElement Flex()
        {
            return Element()
                .SetName("Flex")
                .SetFlexShrink(1f)
                .SetFlexGrow(1f)
                .SetPadding(5, 5, 5, 5);
        }

        public UIElement HorizontalElement()
        {
            return Element()
                .SetName("HorizontalElement")
                .SetFlexDirection(FlexDirection.Row);
        }

        public UIElement HorizontalLayout()
        {
            return HorizontalElement().Expand();
        }

        public UIElement VerticalElement()
        {
            return Element()
                .SetName("VerticalElement")
                .SetFlexDirection(FlexDirection.Column);
        }

        public UIElement VerticalLayout()
        {
            return VerticalElement().Expand();
        }

        public UIElement Panel()
        {
            return Element()
                .SetName("Panel")
                .SetBackgroundColor(PanelBackground)
                .SetBorderWidth(1)
                .SetBorderColor(PanelBorder)
                .SetBorderRadius(5)
                .SetPadding(5, 10, 5, 10)
                .SetOverflow(Overflow.Hidden);
        }

        public UIElement HorizontalPanel()
        {
            return Panel()
                .SetName("HorizontalPanel")
                .SetFlexDirection(FlexDirection.Row);
        }

        public UIElement VerticalPanel()
        {
            return Panel()
                .SetName("VerticalPanel")
                .SetFlexDirection(FlexDirection.Column);
        }

        public UIElement TitlePanel()
        {
            return Panel()
                .SetName("TitlePanel")
                .SetFlexDirection(FlexDirection.Row)
                .SetFlexShrink(0)
                .SetBackgroundColor(TitleBackground)
                .SetBorderColor(PanelBorder)
                .SetBorderWidth(1, 1, 0, 1)
                .SetPadding(5, 10, 5, 10);
        }

        public UIElement HeaderPanel()
        {
            return Panel()
                .SetName("HeaderPanel")
                .SetFlexDirection(FlexDirection.Row)
                .SetFlexShrink(0)
                .SetBackgroundColor(HeaderBackground)
                .SetBorderRadius(5, 5, 0, 0)
                .SetBorderColor(PanelBorder)
                .SetBorderWidth(1, 1, 0, 1)
                .SetPadding(5, 10, 5, 10);
        }

        public UIElement HeaderPanel(IUIBinding<bool> openBinding)
        {
            var element = HeaderPanel();

            var openTrigger = new Action<bool>(open =>
            {
                if (open)
                {
                    element.SetBorderRadius(5, 5, 0, 0);
                    element.SetBorderWidth(1, 1, 0, 1);
                }
                else
                {
                    element.SetBorderRadius(5, 5, 5, 5);
                    element.SetBorderWidth(1, 1, 1, 1);
                }
            });

            openBinding.Changed += openTrigger;
            openTrigger(openBinding.Value);
            return element;
        }

        public UIScrollView ScrollPanel(bool includeFooter = true)
        {
            var panel = ScrollView()
                .SetName("ScrollPanel")
                .SetBackgroundColor(PanelBackground)
                .SetBorderColor(PanelBorder)
                .SetBorderWidth(1, 1, 1, 1)
                .SetBorderRadius(0, 0, 5, 5)
                .SetFlexShrink(1)
                .SetFlexGrow(1);

            if (includeFooter)
            {
                panel.SetBorderRadius(0, 0, 5, 5);
            }
            else
            {
                panel.SetBorderRadius(0, 0, 0, 0);
            }

            return panel;
        }

        public UIElement ScrollContent()
        {
            return Element()
                .SetName("ScrollContent")
                .SetPadding(5)
                .SetHeight(new Length(100, LengthUnit.Percent));
        }

        public UIElement ContentPanel(bool includeFooter = true)
        {
            var panel = VerticalElement()
                .SetName("ContentPanel")
                .SetBackgroundColor(PanelBackground)
                .SetBorderColor(PanelBorder)
                .SetBorderWidth(1, 1, 1, 1)
                .SetBorderRadius(0, 0, 5, 5)
                .SetPadding(8);

            if (includeFooter)
            {
                panel.SetBorderRadius(0, 0, 5, 5);
            }
            else
            {
                panel.SetBorderRadius(0, 0, 0, 0);
            }

            return panel;
        }

        public UIElement FooterPanel()
        {
            return VerticalElement()
                .SetName("FooterPanel")
                .SetBackgroundColor(FooterBackground)
                .SetBorderRadius(0, 0, 5, 5)
                .SetBorderColor(PanelBorder)
                .SetBorderWidth(0, 1, 1, 1)
                .SetMinHeight(5)
                .SetFlexDirection(FlexDirection.Row)
                .SetFlexShrink(0)
                .SetPadding(5, 10, 5, 10);
        }

        public UIElement ErrorPanel()
        {
            return Panel()
                .SetName("ErrorPanel")
                .SetFlexDirection(FlexDirection.Row)
                .SetBackgroundColor(ErrorBackground);
        }

        public UIElement SuccessPanel()
        {
            return Panel()
                .SetName("SuccessPanel")
                .SetFlexDirection(FlexDirection.Row)
                .SetBackgroundColor(SuccessBackground);
        }

        public UIElement InfoPanel()
        {
            return Panel()
                .SetName("InfoPanel")
                .SetFlexDirection(FlexDirection.Row)
                .SetBackgroundColor(InfoBackground);
        }

        public UIElement WarningPanel()
        {
            return Panel()
                .SetName("WarningPanel")
                .SetFlexDirection(FlexDirection.Row)
                .SetBackgroundColor(WarningBackground);
        }

        public UIElement Highlight()
        {
            return Panel()
                .SetName("Highlight")
                .SetFlexDirection(FlexDirection.Row)
                .SetBackgroundColor(HighlightBackground);
        }

        public UIElement Block()
        {
            return Element()
                .SetName("Block")
                .SetHorizontal()
                .SetBorderRadius(3)
                .SetBorderWidth(1)
                .SetMargin(1)
                .SetBorderColor(BlockBorder)
                .SetPadding(5)
                .SetCustomStyle(
                    normal: element => element.SetBackgroundColor(BlockBackground),
                    hover: element => element.SetBackgroundColor(BlockHoverBackground));
        }

        public UIScope HorizontalScope()
        {
            return HorizontalElement()
                .SetName("HorizontalScope")
                .Scope();
        }

        public UIScope VerticalScope()
        {
            return VerticalElement()
                .SetName("VerticalScope")
                .Scope();
        }

        public UIScope EnabledScope()
        {
            return HorizontalElement()
                .SetName("EnabledScope")
                .SetEnabled()
                .Scope();
        }

        public UIScope DisabledScope()
        {
            return HorizontalElement()
                .SetName("DisabledScope")
                .SetDisabled()
                .Scope();
        }

        public UIScope EnabledScope(Func<bool> enabled)
        {
            return HorizontalElement()
                .SetName("EnabledScope")
                .BindEnable(BindReadOnly(enabled))
                .Scope();
        }

        public UISelectionGrid SelectionGrid(string[] selections)
        {
            return Add(new UISelectionGrid(this, selections)
                .SetName("SelectionGrid"));
        }

        public UISelectionGrid<T> SelectionGrid<T>() where T : Enum
        {
            return Add(new UISelectionGrid<T>(this)
                .SetName($"SelectionGrid<{typeof(T).Name}>"));
        }
    }

    class UISelectionGrid<T> : UIEditorElement<VisualElement> where T : Enum
    {
        public event Action<T> SelectionChange;
        public T Current { get; private set; }

        internal UISelectionGrid(UIEditorService ui) : base(ui)
        {
            var selections = (T[])Enum.GetValues(typeof(T));

            using (Scope())
            {
                for (int i = 0; i != selections.Length; ++i)
                {
                    var selection = selections[i];
                    var selectionString = selection.ToString();
                    var button = UI.Button()
                        .SetName(selectionString)
                        .BindEnable(UI.BindReadOnly(() => !Current.Equals(selection)))
                        .SetText(selectionString)
                        .SetClickCallback(() => SetSelection(selection));
                }
            }
        }

        public UISelectionGrid<T> RegisterSelectionCallback(Action<T> action)
        {
            SelectionChange += action;
            return this;
        }

        protected UISelectionGrid<T> SetSelection(T selection)
        {
            if (!Current.Equals(selection))
            {
                Current = selection;
                SelectionChange?.Invoke(Current);
            }

            return this;
        }

        public UISelectionGrid<T> BindValue(IUIBinding<T> binding)
        {
            return BindingUtils.Bind(this, (value) => SetSelection(value), (value) => SelectionChange += value, binding);
        }
    }

    class UISelectionGrid : UIEditorElement<VisualElement>
    {
        public event Action<int> ActiveChange;
        public string[] Selections { get; }
        public int Current { get; private set; }

        internal UISelectionGrid(UIEditorService ui, string[] selections) : base(ui)
        {
            Selections = selections;

            using (Scope())
            {
                for (int i = 0; i != Selections.Length; ++i)
                {
                    var buttonIndex = i;
                    var button = UI.Button()
                        .SetName(Selections[i])
                        .BindEnable(UI.BindReadOnly(() => Current != i))
                        .SetText(Selections[i])
                        .SetClickCallback(() => SetIndex(buttonIndex));
                }
            }
        }

        public UISelectionGrid RegisterSelectionCallback(Action<int> action)
        {
            ActiveChange += action;
            return this;
        }

        protected virtual UISelectionGrid SetIndex(int index)
        {
            Current = index;
            ActiveChange?.Invoke(Current);
            return this;
        }

        public UISelectionGrid BindValue(IUIBinding<int> binding)
        {
            return BindingUtils.Bind(this, (value) => SetIndex(value), binding);
        }
    }
    #endregion

    #region Service
    /// <summary>
    /// Service providing fluent facade to UI elements.
    /// </summary>
    class UIService
    {
        /// <summary>
        /// The current parent element when using scopes.
        /// </summary>
        public UIElement CurrentElement { get; private set; }

        protected List<IUIAnimation> Animations = new List<IUIAnimation>();
        protected List<IUIBinding> Bindings = new List<IUIBinding>();
        protected Stack<UIScope> Scopes = new Stack<UIScope>();

        public UIService()
        {
        }

        public void Update()
        {
            UpdateBindings();
            UpdateAnimations();
        }

        protected virtual void UpdateBindings()
        {
            foreach (var binding in Bindings)
            {
                try
                {
                    binding.Update();
                }
                catch (Exception)
                {
                    Debug.Log("Binding update exception");
                }
            }
        }

        protected virtual void UpdateAnimations()
        {
            foreach (var animation in Animations)
            {
                try
                {
                    animation.Update(Time.deltaTime);
                }
                catch (Exception)
                {
                    Debug.Log("Animation update exception");
                }
            }
        }

        protected T Add<T>(T element) where T : UIElement
        {
            CurrentElement?.Add(element);
            return element;
        }

        #region Elements
        public virtual UIVisualElement Element()
        {
            return Add(new UIVisualElement(this));
        }

        public virtual UIVisualElement Element(VisualElement element)
        {
            return Add(new UIVisualElement(this, element));
        }

        public virtual UIButton Button()
        {
            return Add(new UIButton(this));
        }

        public virtual UILabel Label()
        {
            return Add(new UILabel(this));
        }

        public virtual UIBox Box()
        {
            return Add(new UIBox(this));
        }

        public virtual UIBoundsField BoundsField()
        {
            return Add(new UIBoundsField(this));
        }

        public virtual UIBoundsIntField BoundsIntField()
        {
            return Add(new UIBoundsIntField(this));
        }

        public virtual UIFloatField FloatField()
        {
            return Add(new UIFloatField(this));
        }

        public virtual UIDoubleField DoubleField()
        {
            return Add(new UIDoubleField(this));
        }

        public virtual UIEnumField EnumField()
        {
            return Add(new UIEnumField(this));
        }

        public virtual UIFoldout Foldout()
        {
            return Add(new UIFoldout(this));
        }

        public virtual UIHelpBox HelpBox()
        {
            return Add(new UIHelpBox(this));
        }

        public virtual UIIMGUIContainer IMGUIContainer()
        {
            return Add(new UIIMGUIContainer(this));
        }

        public virtual UIImage Image()
        {
            return Add(new UIImage(this));
        }

        public virtual UIIntegerField IntegerField()
        {
            return Add(new UIIntegerField(this));
        }

        public virtual UIListView ListView()
        {
            return Add(new UIListView(this));
        }

        public virtual UIMinMaxSlider MinMaxSlider()
        {
            return Add(new UIMinMaxSlider(this));
        }

        public virtual UIPopupWindow PopupWindow()
        {
            return Add(new UIPopupWindow(this));
        }

        public virtual UIProgressBar ProgressBar()
        {
            return Add(new UIProgressBar(this));
        }

        public virtual UIRectIntField RectIntField()
        {
            return Add(new UIRectIntField(this));
        }

        public virtual UIRepeatButton RepeatButton()
        {
            return Add(new UIRepeatButton(this));
        }

        public virtual UIScrollView ScrollView()
        {
            return Add(new UIScrollView(this));
        }

        public virtual UIScroller Scroller()
        {
            return Add(new UIScroller(this));
        }

        public virtual UISlider Slider()
        {
            return Add(new UISlider(this));
        }

        public virtual UISliderInt SliderInt()
        {
            return Add(new UISliderInt(this));
        }

        public virtual UITextElement TextElement()
        {
            return Add(new UITextElement(this));
        }

        public virtual UITextField TextField()
        {
            return Add(new UITextField(this));
        }

        public virtual UITemplateContainer TemplateContainer()
        {
            return Add(new UITemplateContainer(this));
        }

        public virtual UIToggle Toggle()
        {
            return Add(new UIToggle(this));
        }

        public virtual UISplitView TwoPaneSplitView()
        {
            return Add(new UISplitView(this));
        }

        public virtual UIVector2Field Vector2Field()
        {
            return Add(new UIVector2Field(this));
        }

        public virtual UIVector2IntField Vector2IntField()
        {
            return Add(new UIVector2IntField(this));
        }

        public virtual UIVector3Field Vector3Field()
        {
            return Add(new UIVector3Field(this));
        }

        public virtual UIVector3IntField Vector3IntField()
        {
            return Add(new UIVector3IntField(this));
        }

        public virtual UIVector4Field Vector4Field()
        {
            return Add(new UIVector4Field(this));
        }

        public virtual UISelectableLabel SelectableLabel()
        {
            return Add(new UISelectableLabel(this));
        }

        public virtual UISnippet Snippet()
        {
            return Add(new UISnippet(this));
        }

#if UNITY_2021_3_OR_NEWER
        public virtual UIDropdownField DropdownField()
        {
            return Add(new UIDropdownField(this));
        }

        public virtual UIGroupBox GroupBox()
        {
            return Add(new UIGroupBox(this));
        }

        public virtual UIHash128Field Hash128Field()
        {
            return Add(new UIHash128Field(this));
        }

        public virtual UIRadioButton RadioButton()
        {
            return Add(new UIRadioButton(this));
        }

        public virtual UIRadioButtonGroup RadioButtonGroup()
        {
            return Add(new UIRadioButtonGroup(this));
        }

        public virtual UIRectField RectField()
        {
            return Add(new UIRectField(this));
        }

#endif

#if UNITY_2022_3_OR_NEWER
        public virtual UIMultiColumnListView MultiColumnListView()
        {
            return Add(new UIMultiColumnListView(this));
        }

        public virtual UIMultiColumnTreeView MultiColumnTreeView()
        {
            return Add(new UIMultiColumnTreeView(this));
        }

        public virtual UITreeView TreeView()
        {
            return Add(new UITreeView(this));
        }

        public virtual UIUnsignedIntegerField UnsignedIntegerField()
        {
            return Add(new UIUnsignedIntegerField(this));
        }

        public virtual UIUnsignedLongField UnsignedLongField()
        {
            return Add(new UIUnsignedLongField(this));
        }

#endif

#if UNITY_2023_2_OR_NEWER
        public virtual UITab Tab()
        {
            return Add(new UITab(this));
        }

        public virtual UITabView TabView()
        {
            return Add(new UITabView(this));
        }

        public virtual UITabView TabView<T>()
        {
            return Add(new UITabView(this));
        }

#endif

        #endregion

        #region Animations
        public void RegisterAnimation(IUIAnimation animation)
        {
            Animations.Add(animation);
        }

        public void UnregisterAnimation(IUIAnimation animation)
        {
            Animations.Remove(animation);
        }

        #endregion

        #region Scopes
        /// <summary>
        /// Sets the element as a parent to future elements created while the scope is active and at the top of the stack.
        /// </summary>
        /// <param name="element">The element</param>
        /// <returns>The scope</returns>
        public UIScope Scope(VisualElement element)
        {
            CurrentElement = new UIVisualElement(this, element);
            var scope = new UIScope(CurrentElement);
            scope.Release += (scope) => ReleaseScope(scope);
            Scopes.Push(scope);
            return scope;
        }

        /// <summary>
        /// Sets the element as a parent to future elements created while the scope is active and at the top of the stack.
        /// </summary>
        /// <param name="element">The element</param>
        /// <returns>The scope</returns>
        public UIScope Scope(UIElement element)
        {
            CurrentElement = element;
            var scope = new UIScope(CurrentElement);
            scope.Release += (scope) => ReleaseScope(scope);
            Scopes.Push(scope);
            return scope;
        }

        void ReleaseScope(UIScope scope)
        {
            if (Scopes.Count == 0)
                throw new Exception("Couldn't release scope as the scope stack is empty.");

            if (Scopes.Peek() == scope)
            {
                Scopes.Pop();
                CurrentElement = Scopes.Count > 0 ? Scopes.Peek().Element : null;
            }
            else
            {
                throw new Exception("Couldn't release scope as it isn't on the top of the stack.");
            }
        }

        #endregion

        #region Bindings
        /// <summary>
        /// The binding contains the value and owns it.
        /// </summary>
        /// <typeparam name="T">The type of the data</typeparam>
        /// <param name="initialValue">The initial value assigned.</param>
        /// <param name="callback">A callback triggered when the value changes</param>
        /// <returns>The binding</returns>
        public IUIBinding<T> BindValue<T>(T initialValue = default, Action<T> callback = null)
        {
            var binding = new ValueUIBinding<T>(initialValue, callback);
            Bindings.Add(binding);
            return binding;
        }

        /// <summary>
        /// The binding will only read the value from the source.
        /// </summary>
        /// <typeparam name="T">The type of the data</typeparam>
        /// <param name="getter">The method to retrieve the value</param>
        /// <param name="callback">A callback triggered when the value changes</param>
        /// <returns>The binding</returns>
        public IUIBinding<T> BindReadOnly<T>(Func<T> getter, Action<T> callback = null)
        {
            var binding = new TargetUIBinding<T>(getter, null, callback);
            Bindings.Add(binding);
            return binding;
        }

        /// <summary>
        /// The binding will read and write to the source.
        /// </summary>
        /// <typeparam name="T">The type of the data</typeparam>
        /// <param name="getter">The method to retrieve the value</param>
        /// <param name="setter">The method to update the value</param>
        /// <param name="callback">A callback triggered when the value changes</param>
        /// <returns>The binding</returns>
        public IUIBinding<T> BindTarget<T>(Func<T> getter, Action<T> setter, Action<T> callback = null)
        {
            var binding = new TargetUIBinding<T>(getter, setter, callback);
            Bindings.Add(binding);
            return binding;
        }

        /// <summary>
        /// The binding will read and optionally write to the source. The expression must target a field or property.
        /// </summary>
        /// <typeparam name="S">The type of the source object</typeparam>
        /// <typeparam name="T">The type of the data</typeparam>
        /// <param name="source">The source object</param>
        /// <param name="expression">The expression to access the value</param>
        /// <param name="callback">A callback triggered when the value changes</param>
        /// <returns>The binding</returns>
        /// <exception cref="Exception">If the expression does not target a field or property</exception>
        public IUIBinding<T> BindTarget<S, T>(S source, Expression<Func<T>> expression, Action<T> callback = null)
        {
            var body = expression.Body as MemberExpression;

            if (body.Member is FieldInfo)
                return BindField(source, body.Member as FieldInfo, callback);
            else if (body.Member is PropertyInfo)
                return BindProperty(source, body.Member as PropertyInfo, callback);

            throw new Exception("Binding failed, expression is not a field or property.");
        }

        /// <summary>
        /// Binds to a property by property name
        /// </summary>
        /// <typeparam name="T">The type of the data</typeparam>
        /// <param name="source">The source object</param>
        /// <param name="propertyName">The name of the property</param>
        /// <param name="callback">A callback triggered when the value changes</param>
        /// <returns>The binding</returns>
        public IUIBinding<T> BindProperty<T>(object source, string propertyName, Action<T> callback = null)
        {
            try
            {
                var propertyInfo = source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return BindProperty(source, propertyInfo, callback);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed binding on property '{propertyName}'. {e}");
            }

            return null;
        }

        /// <summary>
        /// Binds to a property by property info
        /// </summary>
        /// <typeparam name="T">The type of the data</typeparam>
        /// <param name="source">The source object</param>
        /// <param name="propertyInfo">The property info</param>
        /// <param name="callback">A callback triggered when the value changes</param>
        /// <returns>The binding</returns>
        public IUIBinding<T> BindProperty<T>(object source, PropertyInfo propertyInfo, Action<T> callback = null)
        {
            try
            {
                var getMethod = propertyInfo.GetGetMethod(true);
                var getter = getMethod != null ? (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), source, getMethod) : null;
                var setMethod = propertyInfo.GetSetMethod(true);
                var setter = setMethod != null ? (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), source, setMethod) : null;
                var binding = new TargetUIBinding<T>(getter, setter, callback);
                Bindings.Add(binding);
                return binding;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed binding on property '{propertyInfo.Name}'. {e}");
            }

            return null;
        }

        /// <summary>
        /// Binds a property by expression
        /// </summary>
        /// <typeparam name="T">The type of the data</typeparam>
        /// <param name="expression">The expression</param>
        /// <param name="callback">A callback triggered when the value changes</param>
        /// <returns>The binding</returns>
        public IUIBinding<T> BindProperty<T>(Expression<Func<T>> expression, Action<T> callback = null)
        {
            var body = expression.Body as MemberExpression;
            var getSource = Expression.Lambda<Func<object>>(body.Expression).Compile();
            var source = getSource();
            var propertyInfo = body.Member as PropertyInfo;
            return BindProperty(source, propertyInfo, callback);
        }

        /// <summary>
        /// Binds a field by property name
        /// </summary>
        /// <typeparam name="S">The type of the source object</typeparam>
        /// <typeparam name="T">The type of the data</typeparam>
        /// <param name="source">The source object</param>
        /// <param name="fieldName">The name of the field</param>
        /// <param name="callback">A callback triggered when the value changes</param>
        /// <returns>The binding</returns>
        public IUIBinding<T> BindField<S, T>(S source, string fieldName, Action<T> callback = null)
        {
            try
            {
                var type = typeof(S);
                var fieldInfo = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                var instExp = Expression.Parameter(type);
                var fieldExp = Expression.Field(instExp, fieldInfo);
                var getter = Expression.Lambda<Func<S, T>>(fieldExp, instExp).Compile();
                var valueExp = Expression.Parameter(typeof(T));
                var setter = Expression.Lambda<Action<S, T>>(Expression.Assign(fieldExp, valueExp), instExp, valueExp).Compile();

                var binding = new TargetUIBinding<T>(() => getter(source), (value) => setter(source, value), callback);
                Bindings.Add(binding);
                return binding;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed binding on field '{fieldName}'. {e}");
            }

            return null;
        }

        /// <summary>
        /// Binds a field by field info
        /// </summary>
        /// <typeparam name="S">The type of the source object</typeparam>
        /// <typeparam name="T">The type of the data</typeparam>
        /// <param name="source">The source object</param>
        /// <param name="fieldInfo">The info of the field</param>
        /// <param name="callback">A callback triggered when the value changes</param>
        /// <returns>The binding</returns>
        public IUIBinding<T> BindField<S, T>(S source, FieldInfo fieldInfo, Action<T> callback = null)
        {
            try
            {
                var type = source.GetType();
                var instExp = Expression.Parameter(type);
                var fieldExp = Expression.Field(instExp, fieldInfo);
                var getter = Expression.Lambda<Func<S, T>>(fieldExp, instExp).Compile();
                var valueExp = Expression.Parameter(typeof(T));
                var setter = Expression.Lambda<Action<S, T>>(Expression.Assign(fieldExp, valueExp), instExp, valueExp).Compile();

                var binding = new TargetUIBinding<T>(() => getter(source), (value) => setter(source, value), callback);
                Bindings.Add(binding);
                return binding;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed binding on field '{fieldInfo.Name}'. {e}");
            }

            return null;
        }

        /// <summary>
        /// Binds a field by expression
        /// </summary>
        /// <typeparam name="S">The type of the source object</typeparam>
        /// <typeparam name="T">The type of the data</typeparam>
        /// <param name="source">The source object</param>
        /// <param name="expression">The expression</param>
        /// <param name="callback">A callback triggered when the value changes</param>
        /// <returns>The binding</returns>
        public IUIBinding<T> BindField<S, T>(S source, Expression<Func<T>> expression, Action<T> callback = null)
        {
            var body = expression.Body as MemberExpression;
            return BindField(source, body.Member as FieldInfo, callback);
        }

        #endregion
    }
    #endregion

    #region Elements
    class UIElement
    {
        protected internal UIService UI { get; protected set; }
        public VisualElement Element { get; protected set; }

        protected internal UIElement(UIService ui)
        {
            UI = ui;
        }

        public UIScope Scope()
        {
            return UI.Scope(this);
        }

        public UIElement GetChild(int index)
        {
            return new UIElement<VisualElement>(UI, Element[index]);
        }

        public UIElement Clear()
        {
            Element.Clear();
            return this;
        }

        public UIElement GetChild(string name)
        {
            foreach (var child in Element.Children())
            {
                if (child.name == name)
                {
                    return new UIElement<VisualElement>(UI, child);
                }
            }

            return null;
        }

        public UIElement this[string name] => GetChild(name);
        public UIElement this[int index] => GetChild(index);
    }

    class UIElement<T> : UIElement where T : VisualElement, new()
    {
        public new T Element { get; }

        protected internal UIElement(UIService ui) : base(ui)
        {
            Element = new T();
            base.Element = Element;
        }

        protected internal UIElement(UIService ui, T value) : base(ui)
        {
            UI = ui;
            Element = value;
            base.Element = Element;
        }
    }

    class UISplitView : UIElement<TwoPaneSplitView>
    {
        public UIElement First { get; }
        public UIElement Second { get; }

        internal UISplitView(UIService ui) : base(ui)
        {
            First = new UIVisualElement(UI);
            Second = new UIVisualElement(UI);

            Element.Add(First.Element);
            Element.Add(Second.Element);
            Element.fixedPaneIndex = 0;
            Element.orientation = TwoPaneSplitViewOrientation.Horizontal;
        }
    }

    class UISelectableLabel : UITextField
    {
        internal UISelectableLabel(UIService ui) : base(ui)
        {
            this.SetMultiline()
                .SetReadOnly()
                .SetupTextInput(input =>
                {
                    input.SetBorderWidth(0)
                        .SetBackgroundColor(new Color(0, 0, 0, 0));
                });
        }

        internal UISelectableLabel(UIService ui, TextField element) : base(ui, element) {}
    }

    class UISnippet : UIElement<VisualElement>
    {
        readonly UILabel m_TypeElement;
        readonly UITextField m_CodeTextField;

        internal UISnippet(UIService ui) : base(ui)
        {
            using (Scope())
            using (UI.Element()
                   .SetFlexDirection(FlexDirection.Row)
                   .SetOverflow(Overflow.Hidden)
                   .SetBackgroundColor(new Color(0.15f, 0.15f, 0.3f, 0.1f))
                   .SetBorderColor(new Color(0.4f, 0.4f, 0.4f, 0.5f))
                   .SetBorderRadius(4)
                   .SetBorderWidth(1f)
                   .Scope())
            {
                m_TypeElement = UI.Label()
                    .SetText("C#")
                    .SetPadding(5)
                    .SetMinWidth(30)
                    .SetTextAlign(TextAnchor.UpperRight)
                    .SetFontSize(12)
                    .SetBackgroundColor(new Color(0f, 0f, 0f, 0.1f))
                    .SetBorderColor(new Color(0.4f, 0.4f, 0.4f, 0.5f));

                m_CodeTextField = UI.TextField()
                    .SetPadding(4, 10, 4, 0)
                    .SetFlexGrow(1)
                    .SetMultiline()
                    .SetWhitespace(WhiteSpace.Normal)
                    .SetBorderWidth(0)
                    .SetReadOnly()
                    .SetFontSize(12);

                for (var i = 0; i != m_CodeTextField.Element.childCount; ++i)
                {
                    var child = m_CodeTextField.Element.Children().ElementAt(i);
                    child.style.backgroundColor = new Color(0, 0, 0, 0);
                    child.style.borderRightWidth = 0;
                    child.style.borderLeftWidth = 0;
                    child.style.borderTopWidth = 0;
                    child.style.borderBottomWidth = 0;
                }
            }
        }

        public UISnippet SetType(string text)
        {
            m_TypeElement.SetText(text);
            return this;
        }

        public UISnippet SetText(string text)
        {
            m_CodeTextField.SetText(text);
            return this;
        }
    }

    class UITextField : UIElement<TextField>
    {
        internal UITextField(UIService ui) : base(ui) {}
        internal UITextField(UIService ui, TextField element) : base(ui, element) {}
    }

    internal static class UITextFieldExtensions
    {
        public static T BindValue<T>(this T element, IUIBinding<string> binding) where T : UITextField
        {
            return BindingUtils.BindValue<T, TextField, string>(element, binding);
        }

        public static T SetText<T>(this T element, string text) where T : UITextField
        {
            element.Element.value = text;
            return element;
        }

        public static T SetReadOnly<T>(this T element, bool isReadOnly = true) where T : UITextField
        {
            element.Element.isReadOnly = isReadOnly;
            return element;
        }

        public static T SetMultiline<T>(this T element, bool multiline = true) where T : UITextField
        {
            element.Element.multiline = multiline;
            return element;
        }

        public static T SetPasswordField<T>(this T element, bool isPasswordField = true) where T : UITextField
        {
            element.Element.isPasswordField = isPasswordField;
            return element;
        }

        public static T SetMaxLength<T>(this T element, int maxLength) where T : UITextField
        {
            element.Element.maxLength = maxLength;
            return element;
        }

        public static T SetupTextInput<T>(this T element, Action<UIElement> func) where T : UITextField
        {
            func(element.GetChild("unity-text-input"));
            return element;
        }

#if UNITY_2022_3_OR_NEWER
        public static T SelectAllOnFocus<T>(this T element, bool value) where T : UITextField
        {
            element.Element.selectAllOnFocus = value;
            return element;
        }

#endif
    }

    internal static class UIIntegerFieldExtensions
    {
        public static T BindValue<T>(this T element, IUIBinding<int> binding) where T : UIIntegerField
        {
            return BindingUtils.BindValue<T, IntegerField, int>(element, binding);
        }
    }

    internal static class UILongFieldExtensions
    {
        public static T BindValue<T>(this T element, IUIBinding<long> binding) where T : UILongField
        {
            return BindingUtils.BindValue<T, LongField, long>(element, binding);
        }
    }

    class UIVisualElement : UIElement<VisualElement>
    {
        internal UIVisualElement(UIService ui) : base(ui) {}
        internal UIVisualElement(UIService ui, VisualElement element) : base(ui, element) {}
    }

    class UILabel : UIElement<Label>
    {
        internal UILabel(UIService ui) : base(ui) {}
        internal UILabel(UIService ui, Label element) : base(ui, element) {}
    }

    class UIBox : UIElement<Box>
    {
        internal UIBox(UIService ui) : base(ui) {}
        internal UIBox(UIService ui, Box element) : base(ui, element) {}
    }

    class UIBoundsField : UIElement<BoundsField>
    {
        internal UIBoundsField(UIService ui) : base(ui) {}
        internal UIBoundsField(UIService ui, BoundsField element) : base(ui, element) {}
    }

    class UIBoundsIntField : UIElement<BoundsIntField>
    {
        internal UIBoundsIntField(UIService ui) : base(ui) {}
        internal UIBoundsIntField(UIService ui, BoundsIntField element) : base(ui, element) {}
    }

    class UIFloatField : UIElement<FloatField>
    {
        internal UIFloatField(UIService ui) : base(ui) {}
        internal UIFloatField(UIService ui, FloatField element) : base(ui, element) {}
    }

    class UIDoubleField : UIElement<DoubleField>
    {
        internal UIDoubleField(UIService ui) : base(ui) {}
        internal UIDoubleField(UIService ui, DoubleField element) : base(ui, element) {}
    }

    class UIEnumField : UIElement<EnumField>
    {
        internal UIEnumField(UIService ui) : base(ui) {}
        internal UIEnumField(UIService ui, EnumField element) : base(ui, element) {}
    }

    internal static class UIEnumFieldExtensions
    {
        public static T Init<T>(this T element, Enum value) where T : UIEnumField
        {
            element.Element.Init(value);
            return element;
        }

        public static T SetValue<T>(this T element, Enum value) where T : UIEnumField
        {
            element.Element.value = value;
            return element;
        }

        public static T BindValue<T>(this T element, IUIBinding<Enum> binding) where T : UIEnumField
        {
            return BindingUtils.BindValue<T, EnumField, Enum>(element, binding);
        }
    }

    class UIFoldout : UIElement<Foldout>
    {
        internal UIFoldout(UIService ui) : base(ui) {}
        internal UIFoldout(UIService ui, Foldout element) : base(ui, element) {}
    }

    class UIHelpBox : UIElement<HelpBox>
    {
        internal UIHelpBox(UIService ui) : base(ui) {}
        internal UIHelpBox(UIService ui, HelpBox element) : base(ui, element) {}
    }

    internal static class UIHelpBoxExtensions
    {
        public static T SetText<T>(this T element, string text) where T : UIHelpBox
        {
            element.Element.text = text;
            return element;
        }

        public static T SetMessageType<T>(this T element, HelpBoxMessageType type) where T : UIHelpBox
        {
            element.Element.messageType = type;
            return element;
        }
    }

    class UIIMGUIContainer : UIElement<IMGUIContainer>
    {
        internal UIIMGUIContainer(UIService ui) : base(ui) {}
        internal UIIMGUIContainer(UIService ui, IMGUIContainer element) : base(ui, element) {}
    }

    class UIImage : UIElement<Image>
    {
        internal UIImage(UIService ui) : base(ui) {}
        internal UIImage(UIService ui, Image element) : base(ui, element) {}
    }

    class UIIntegerField : UIElement<IntegerField>
    {
        internal UIIntegerField(UIService ui) : base(ui) {}
        internal UIIntegerField(UIService ui, IntegerField element) : base(ui, element) {}
    }

    class UIListView : UIElement<ListView>
    {
        internal UIListView(UIService ui) : base(ui) {}
        internal UIListView(UIService ui, ListView element) : base(ui, element) {}
    }

    class UIMinMaxSlider : UIElement<MinMaxSlider>
    {
        internal UIMinMaxSlider(UIService ui) : base(ui) {}
        internal UIMinMaxSlider(UIService ui, MinMaxSlider element) : base(ui, element) {}
    }

    class UIPopupWindow : UIElement<UnityEngine.UIElements.PopupWindow>
    {
        internal UIPopupWindow(UIService ui) : base(ui) {}
        internal UIPopupWindow(UIService ui, UnityEngine.UIElements.PopupWindow element) : base(ui, element) {}
    }

    class UIProgressBar : UIElement<ProgressBar>
    {
        internal UIProgressBar(UIService ui) : base(ui) {}
        internal UIProgressBar(UIService ui, ProgressBar element) : base(ui, element) {}
    }

    class UIRectIntField : UIElement<RectIntField>
    {
        internal UIRectIntField(UIService ui) : base(ui) {}
        internal UIRectIntField(UIService ui, RectIntField element) : base(ui, element) {}
    }

    class UIRepeatButton : UIElement<RepeatButton>
    {
        internal UIRepeatButton(UIService ui) : base(ui) {}
        internal UIRepeatButton(UIService ui, RepeatButton element) : base(ui, element) {}
    }

    class UIScrollView : UIElement<ScrollView>
    {
        internal UIScrollView(UIService ui) : base(ui) { Setup(); }
        internal UIScrollView(UIService ui, ScrollView scrollView) : base(ui, scrollView) { Setup(); }

        void Setup()
        {
            Element.style.flexGrow = 1;
            Element.contentContainer.style.flexGrow = 1;
        }

        public UIScrollView SetupVerticalScroller(Action<UIScroller> func)
        {
            func(new UIScroller(UI, Element.verticalScroller));
            return this;
        }

        /// <inheritdoc cref="ScrollView.horizontalScrollerVisibility" />
        public UIScrollView SetupHorizontalScroller(Action<UIScroller> func)
        {
            func(new UIScroller(UI, Element.horizontalScroller));
            return this;
        }

        /// <inheritdoc cref="ScrollView.scrollOffset" />
        public UIScrollView SetScrollOffset(Vector2 value)
        {
            Element.scrollOffset = value;
            return this;
        }

        /// <inheritdoc cref="ScrollView.horizontalPageSize" />
        public UIScrollView SetHorizontalPageSize(float value)
        {
            Element.horizontalPageSize = value;
            return this;
        }

        /// <inheritdoc cref="ScrollView.verticalPageSize" />
        public UIScrollView SetVerticalPageSize(float value)
        {
            Element.verticalPageSize = value;
            return this;
        }

        /// <inheritdoc cref="ScrollView.scrollDecelerationRate" />
        public UIScrollView SetScrollDecelerationRate(float value)
        {
            Element.scrollDecelerationRate = value;
            return this;
        }

        /// <inheritdoc cref="ScrollView.elasticity" />
        public UIScrollView SetElasticity(float value)
        {
            Element.elasticity = value;
            return this;
        }

        /// <inheritdoc cref="ScrollView.touchScrollBehavior" />
        public UIScrollView SetTouchScrollBehavior(ScrollView.TouchScrollBehavior value)
        {
            Element.touchScrollBehavior = value;
            return this;
        }

#if UNITY_2021_3_OR_NEWER
        /// <inheritdoc cref="ScrollView.verticalScrollerVisibility" />
        public UIScrollView SetVerticalScrollerVisibility(ScrollerVisibility value)
        {
            Element.verticalScrollerVisibility = value;
            return this;
        }

        /// <inheritdoc cref="ScrollView.horizontalScrollerVisibility" />
        public UIScrollView SetHorizontalScrollerVisibility(ScrollerVisibility value)
        {
            Element.horizontalScrollerVisibility = value;
            return this;
        }

        /// <inheritdoc cref="ScrollView.mouseWheelScrollSize" />
        public UIScrollView SetMouseWheelScrollSize(float value)
        {
            Element.mouseWheelScrollSize = value;
            return this;
        }

        /// <inheritdoc cref="ScrollView.nestedInteractionKind" />
        public UIScrollView SetNestedInteractionKind(ScrollView.NestedInteractionKind value)
        {
            Element.nestedInteractionKind = value;
            return this;
        }

        /// <inheritdoc cref="ScrollView.mode" />
        public UIScrollView SetScrollViewMode(ScrollViewMode value)
        {
            Element.mode = value;
            return this;
        }

#endif
    }

    class UIScroller : UIElement<Scroller>
    {
        internal UIScroller(UIService ui) : base(ui) {}
        internal UIScroller(UIService ui, Scroller element) : base(ui, element) {}

        public UIScroller HideArrows()
        {
            SetupSlider(slider => slider.SetMargin(0));
            GetChild("unity-low-button").SetDisplay(DisplayStyle.None);
            GetChild("unity-high-button").SetDisplay(DisplayStyle.None);
            return this;
        }

        public UIScroller SetupSlider(Action<UISlider> func)
        {
            func(new UISlider(UI, Element.slider));
            return this;
        }
    }

    class UISlider : UIElement<Slider>
    {
        internal UISlider(UIService ui) : base(ui) {}
        internal UISlider(UIService ui, Slider element) : base(ui, element) {}

        public UISlider SetupTracker(Action<UIElement> func)
        {
            func(this[0]["unity-drag-container"]["unity-tracker"]);
            return this;
        }

        public UISlider SetupDragger(Action<UIElement> func)
        {
            func(this[0]["unity-drag-container"]["unity-dragger"]);
            return this;
        }
    }

    class UISliderInt : UIElement<SliderInt>
    {
        internal UISliderInt(UIService ui) : base(ui) {}
        internal UISliderInt(UIService ui, SliderInt element) : base(ui, element) {}
    }

    class UITextElement : UIElement<TextElement>
    {
        internal UITextElement(UIService ui) : base(ui) {}
        internal UITextElement(UIService ui, TextElement element) : base(ui, element) {}
    }

    class UITemplateContainer : UIElement<TemplateContainer>
    {
        internal UITemplateContainer(UIService ui) : base(ui) {}
        internal UITemplateContainer(UIService ui, TemplateContainer element) : base(ui, element) {}
    }

    class UIToggle : UIElement<Toggle>
    {
        internal UIToggle(UIService ui) : base(ui) {}
        internal UIToggle(UIService ui, Toggle element) : base(ui, element) {}
    }

    class UILongField : UIElement<LongField>
    {
        internal UILongField(UIService ui) : base(ui) {}
        internal UILongField(UIService ui, LongField element) : base(ui, element) {}
    }

    class UIVector2Field : UIElement<Vector2Field>
    {
        internal UIVector2Field(UIService ui) : base(ui) {}
        internal UIVector2Field(UIService ui, Vector2Field element) : base(ui, element) {}
    }

    class UIVector2IntField : UIElement<Vector2IntField>
    {
        internal UIVector2IntField(UIService ui) : base(ui) {}
        internal UIVector2IntField(UIService ui, Vector2IntField element) : base(ui, element) {}
    }

    class UIVector3Field : UIElement<Vector3Field>
    {
        internal UIVector3Field(UIService ui) : base(ui) {}
        internal UIVector3Field(UIService ui, Vector3Field element) : base(ui, element) {}
    }

    class UIVector3IntField : UIElement<Vector3IntField>
    {
        internal UIVector3IntField(UIService ui) : base(ui) {}
        internal UIVector3IntField(UIService ui, Vector3IntField element) : base(ui, element) {}
    }

    class UIVector4Field : UIElement<Vector4Field>
    {
        internal UIVector4Field(UIService ui) : base(ui) {}
        internal UIVector4Field(UIService ui, Vector4Field element) : base(ui, element) {}
    }

    class UIButton : UIElement<Button>
    {
        internal UIButton(UIService ui) : base(ui) {}
        internal UIButton(UIService ui, Button element) : base(ui, element) {}
    }

#if UNITY_2021_3_OR_NEWER
    class UIDropdownField : UIElement<DropdownField>
    {
        internal UIDropdownField(UIService ui) : base(ui) {}
        internal UIDropdownField(UIService ui, DropdownField element) : base(ui, element) {}
    }

    class UIGroupBox : UIElement<GroupBox>
    {
        internal UIGroupBox(UIService ui) : base(ui) {}
        internal UIGroupBox(UIService ui, GroupBox element) : base(ui, element) {}
    }

    class UIHash128Field : UIElement<Hash128Field>
    {
        internal UIHash128Field(UIService ui) : base(ui) {}
        internal UIHash128Field(UIService ui, Hash128Field element) : base(ui, element) {}
    }

    class UIRadioButton : UIElement<RadioButton>
    {
        internal UIRadioButton(UIService ui) : base(ui) {}
        internal UIRadioButton(UIService ui, RadioButton element) : base(ui, element) {}
    }

    class UIRadioButtonGroup : UIElement<RadioButtonGroup>
    {
        internal UIRadioButtonGroup(UIService ui) : base(ui) {}
        internal UIRadioButtonGroup(UIService ui, RadioButtonGroup element) : base(ui, element) {}
    }

    class UIRectField : UIElement<RectField>
    {
        internal UIRectField(UIService ui) : base(ui) {}
        internal UIRectField(UIService ui, RectField element) : base(ui, element) {}
    }
#endif

#if UNITY_2022_3_OR_NEWER
    class UIMultiColumnListView : UIElement<MultiColumnListView>
    {
        internal UIMultiColumnListView(UIService ui) : base(ui) {}
        internal UIMultiColumnListView(UIService ui, MultiColumnListView element) : base(ui, element) {}
    }

    class UIMultiColumnTreeView : UIElement<MultiColumnTreeView>
    {
        internal UIMultiColumnTreeView(UIService ui) : base(ui) {}
        internal UIMultiColumnTreeView(UIService ui, MultiColumnTreeView element) : base(ui, element) {}
    }

    class UITreeView : UIElement<TreeView>
    {
        internal UITreeView(UIService ui) : base(ui) {}
        internal UITreeView(UIService ui, TreeView element) : base(ui, element) {}
    }

    class UIUnsignedIntegerField : UIElement<UnsignedIntegerField>
    {
        internal UIUnsignedIntegerField(UIService ui) : base(ui) {}
        internal UIUnsignedIntegerField(UIService ui, UnsignedIntegerField element) : base(ui, element) {}
    }

    class UIUnsignedLongField : UIElement<UnsignedLongField>
    {
        internal UIUnsignedLongField(UIService ui) : base(ui) {}
        internal UIUnsignedLongField(UIService ui, UnsignedLongField element) : base(ui, element) {}
    }

    internal static class UIUnsignedIntegerFieldExtensions
    {
        public static T BindValue<T>(this T element, IUIBinding<uint> binding) where T : UIUnsignedIntegerField
        {
            return BindingUtils.BindValue<T, UnsignedIntegerField, uint>(element, binding);
        }
    }

    internal static class UIUnsignedLongFieldExtensions
    {
        public static T BindValue<T>(this T element, IUIBinding<ulong> binding) where T : UIUnsignedLongField
        {
            return BindingUtils.BindValue<T, UnsignedLongField, ulong>(element, binding);
        }
    }
#endif

#if UNITY_2023_2_OR_NEWER
    class UITab : UIElement<Tab>
    {
        internal UITab(UIService ui) : base(ui) {}
        internal UITab(UIService ui, Tab element) : base(ui, element) {}

        public UITab SetupTabHeader(Action<UIElement> func)
        {
            func(new UIElement<VisualElement>(UI, Element.tabHeader));
            return this;
        }
    }

    class UITabView : UIElement<TabView>
    {
        internal UITabView(UIService ui) : base(ui) {}
        internal UITabView(UIService ui, TabView element) : base(ui, element) {}
    }
#endif
    #endregion

    #region Extensions
    /// <summary>
    /// Extensions to enable fluent sequencing for ui elements
    /// </summary>
    internal static class UIElementExtensions
    {
        public static T Setup<T>(this T element, Action<T> func) where T : UIElement
        {
            func(element);
            return element;
        }

        public static T AddAnimation<T>(this T element, IUIAnimation animation) where T : UIElement
        {
            animation.Element = element;
            element.UI.RegisterAnimation(animation);
            return element;
        }

        public static T SetName<T>(this T element, string name) where T : UIElement
        {
            element.Element.name = name;
            return element;
        }

        public static T Stretch<T>(this T element) where T : UIElement
        {
            element.SetPosition(Position.Absolute, 0, 0, 0, 0);
            return element;
        }

        public static T ClearClassList<T>(this T element) where T : UIElement
        {
            element.Element.ClearClassList();
            return element;
        }

        public static T Add<T>(this T element, UIElement builder) where T : UIElement
        {
            element.Element.Add(builder.Element);
            return element;
        }

        public static T Add<T>(this T element, UIElement[] builders) where T : UIElement
        {
            foreach (var item in builders)
            {
                element.Element.Add(item.Element);
            }

            return element;
        }

        public static T AddToClassList<T>(this T element, string className) where T : UIElement
        {
            element.Element.AddToClassList(className);
            return element;
        }

        public static T RemoveFromClassList<T>(this T element, string className) where T : UIElement
        {
            element.Element.RemoveFromClassList(className);
            return element;
        }

        public static T SetHeight<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.height = value;
            return element;
        }

        public static T SetHeightPercent<T>(this T element, float height) where T : UIElement
        {
            element.Element.style.height = new Length(height, LengthUnit.Percent);
            return element;
        }

        public static T SetMinHeight<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.minHeight = value;
            return element;
        }

        public static T SetMinHeightPercent<T>(this T element, float height) where T : UIElement
        {
            element.Element.style.minHeight = new Length(height, LengthUnit.Percent);
            return element;
        }

        public static T SetMaxHeight<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.maxHeight = value;
            return element;
        }

        public static T SetMaxHeightPercent<T>(this T element, float height) where T : UIElement
        {
            element.Element.style.maxHeight = new Length(height, LengthUnit.Percent);
            return element;
        }

        public static T SetWidth<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.width = value;
            return element;
        }

        public static T SetWidthPercent<T>(this T element, float width) where T : UIElement
        {
            element.Element.style.width = new Length(width, LengthUnit.Percent);
            return element;
        }

        public static T SetMinWidth<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.minWidth = value;
            return element;
        }

        public static T SetMinWidthPercent<T>(this T element, float width) where T : UIElement
        {
            element.Element.style.minWidth = new Length(width, LengthUnit.Percent);
            return element;
        }

        public static T SetMaxWidth<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.maxWidth = value;
            return element;
        }

        public static T SetMaxWidthPercent<T>(this T element, float width) where T : UIElement
        {
            element.Element.style.maxWidth = new Length(width, LengthUnit.Percent);
            return element;
        }

        public static T SetBackgroundColor<T>(this T element, StyleColor value) where T : UIElement
        {
            element.Element.style.backgroundColor = value;
            return element;
        }

        public static T SetBackgroundImage<T>(this T element, StyleBackground value) where T : UIElement
        {
            element.Element.style.backgroundImage = value;
            return element;
        }

        public static T SetBackgroundImageTint<T>(this T element, StyleColor value) where T : UIElement
        {
            element.Element.style.unityBackgroundImageTintColor = value;
            return element;
        }

        public static T SetBackgroundScaleMode<T>(this T element, StyleEnum<ScaleMode> value) where T : UIElement
        {
#pragma warning disable 618
            element.Element.style.unityBackgroundScaleMode = value;
#pragma warning restore 618
            return element;
        }

        /// <inheritdoc cref="IStyle.alignContent" />
        public static T SetAlignContent<T>(this T element, StyleEnum<Align> value) where T : UIElement
        {
            element.Element.style.alignContent = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.alignItems" />
        public static T SetAlignItems<T>(this T element, StyleEnum<Align> value) where T : UIElement
        {
            element.Element.style.alignItems = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.alignSelf" />
        public static T SetAlignSelf<T>(this T element, StyleEnum<Align> value) where T : UIElement
        {
            element.Element.style.alignSelf = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.cursor" />
        public static T SetCursor<T>(this T element, StyleCursor value) where T : UIElement
        {
            element.Element.style.cursor = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.justifyContent" />
        public static T SetJustifyContent<T>(this T element, StyleEnum<Justify> value) where T : UIElement
        {
            element.Element.style.justifyContent = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.opacity" />
        public static T SetOpacity<T>(this T element, StyleFloat value) where T : UIElement
        {
            element.Element.style.opacity = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.unityOverflowClipBox" />
        public static T SetOverflowClipBox<T>(this T element, StyleEnum<OverflowClipBox> value) where T : UIElement
        {
            element.Element.style.unityOverflowClipBox = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.whiteSpace" />
        public static T SetWhitespace<T>(this T element, StyleEnum<WhiteSpace> value) where T : UIElement
        {
            element.Element.style.whiteSpace = value;
            return element;
        }

#if UNITY_2021_3_OR_NEWER
        public static T SetTransitionDelay<T>(this T element, StyleList<TimeValue> value) where T : UIElement
        {
            element.Element.style.transitionDelay = value;
            return element;
        }

        public static T SetTransitionDuration<T>(this T element, StyleList<TimeValue> value) where T : UIElement
        {
            element.Element.style.transitionDuration = value;
            return element;
        }

        public static T SetTransitionOrigin<T>(this T element, StyleTransformOrigin value) where T : UIElement
        {
            element.Element.style.transformOrigin = value;
            return element;
        }

        public static T SetTransitionProperty<T>(this T element, StyleList<StylePropertyName> value) where T : UIElement
        {
            element.Element.style.transitionProperty = value;
            return element;
        }

        public static T SetTransitionTimingFunction<T>(this T element, StyleList<EasingFunction> value) where T : UIElement
        {
            element.Element.style.transitionTimingFunction = value;
            return element;
        }

        public static T SetTranslate<T>(this T element, StyleTranslate value) where T : UIElement
        {
            element.Element.style.translate = value;
            return element;
        }

        public static T SetRotate<T>(this T element, StyleRotate value) where T : UIElement
        {
            element.Element.style.rotate = value;
            return element;
        }

        public static T SetScale<T>(this T element, StyleScale value) where T : UIElement
        {
            element.Element.style.scale = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.letterSpacing" />
        public static T SetLetterSpacing<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.letterSpacing = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.transformOrigin" />
        public static T SetTransformOrigin<T>(this T element, StyleTransformOrigin value) where T : UIElement
        {
            element.Element.style.transformOrigin = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.unityParagraphSpacing" />
        public static T SetParagraphSpacing<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.unityParagraphSpacing = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.wordSpacing" />
        public static T SetWordSpacing<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.wordSpacing = value;
            return element;
        }

#endif

#if UNITY_2022_3_OR_NEWER
        public static T SetBackgroundRepeat<T>(this T element, StyleBackgroundRepeat value) where T : UIElement
        {
            element.Element.style.backgroundRepeat = value;
            return element;
        }

        public static T SetBackgroundSize<T>(this T element, StyleBackgroundSize value) where T : UIElement
        {
            element.Element.style.backgroundSize = value;
            return element;
        }

        public static T SetBackgroundPositionX<T>(this T element, StyleBackgroundPosition value) where T : UIElement
        {
            element.Element.style.backgroundPositionX = value;
            return element;
        }

        public static T SetBackgroundPositionY<T>(this T element, StyleBackgroundPosition value) where T : UIElement
        {
            element.Element.style.backgroundPositionY = value;
            return element;
        }

        public static T SetSliceScale<T>(this T element, StyleFloat scale) where T : UIElement
        {
            element.Element.style.unitySliceScale = scale;
            return element;
        }

#endif

        public static T RegisterKeyDownEvent<T>(this T element, Action<T, KeyDownEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterKeyUpEvent<T>(this T element, Action<T, KeyUpEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterInputEvent<T>(this T element, Action<T, InputEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterGeometryChangedEvent<T>(this T element, Action<T, GeometryChangedEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterClickEvent<T>(this T element, Action<T, ClickEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

#if UNITY_STANDALONE
        public static T RegisterDragExitedEvent<T>(this T element, Action<T, DragExitedEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterDragUpdatedEvent<T>(this T element, Action<T, DragUpdatedEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterDragPerformEvent<T>(this T element, Action<T, DragPerformEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterDragEnterEvent<T>(this T element, Action<T, DragEnterEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterDragLeaveEvent<T>(this T element, Action<T, DragLeaveEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

#endif

        public static T RegisterPointerUpEvent<T>(this T element, Action<T, PointerUpEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterPointerDownEvent<T>(this T element, Action<T, PointerDownEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterPointerLeaveEvent<T>(this T element, Action<T, PointerLeaveEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T PointerMoveEvent<T>(this T element, Action<T, PointerMoveEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterPointerEnterEvent<T>(this T element, Action<T, PointerEnterEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterPointerOverEvent<T>(this T element, Action<T, PointerOverEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterPointerOutEvent<T>(this T element, Action<T, PointerOutEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterPointerCancelEvent<T>(this T element, Action<T, PointerCancelEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterPointerCaptureEvent<T>(this T element, Action<T, PointerCaptureEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterPointerCaptureOutEvent<T>(this T element, Action<T, PointerCaptureOutEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterMouseDownEvent<T>(this T element, Action<T, MouseDownEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterMouseUpEvent<T>(this T element, Action<T, MouseUpEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterMouseMoveEvent<T>(this T element, Action<T, MouseMoveEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterWheelEvent<T>(this T element, Action<T, WheelEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterMouseEnterWindowEvent<T>(this T element, Action<T, MouseEnterWindowEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterMouseLeaveWindowEvent<T>(this T element, Action<T, MouseLeaveWindowEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterMouseEnterEvent<T>(this T element, Action<T, MouseEnterEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterMouseLeaveEvent<T>(this T element, Action<T, MouseLeaveEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterMouseOverEvent<T>(this T element, Action<T, MouseOverEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterMouseOutEvent<T>(this T element, Action<T, MouseOutEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterMouseCaptureEvent<T>(this T element, Action<T, MouseCaptureEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterMouseCaptureOutEvent<T>(this T element, Action<T, MouseCaptureOutEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterChangeEvent<T, U>(this T element, Action<T, ChangeEvent<U>> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterAttachToPanelEvent<T>(this T element, Action<T, AttachToPanelEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterDetachFromPanelEvent<T>(this T element, Action<T, DetachFromPanelEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterTooltipEvent<T>(this T element, Action<T, TooltipEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterIMGUIEvent<T>(this T element, Action<T, IMGUIEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterCallback<T, U>(this T element, Action<T, U> action) where U : EventBase<U>, new()
            where T : UIElement
        {
            element.Element.RegisterCallback<U>(pointerEvent => action?.Invoke(element, pointerEvent), TrickleDown.TrickleDown);
            return element;
        }

#if UNITY_2021_3_OR_NEWER
        public static T RegisterTransitionRunEvent<T>(this T element, Action<T, TransitionRunEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterNavigationMoveEvent<T>(this T element, Action<T, NavigationMoveEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterNavigationCancelEvent<T>(this T element, Action<T, NavigationCancelEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterNavigationSubmitEvent<T>(this T element, Action<T, NavigationSubmitEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterTransitionStartEvent<T>(this T element, Action<T, TransitionStartEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterTransitionEndEvent<T>(this T element, Action<T, TransitionEndEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterTransitionCancelEvent<T>(this T element, Action<T, TransitionCancelEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

#endif

        public static T SetCustomStyle<T>(this T element,
            Action<T> normal = null,
            Action<T> hover = null,
            Action<T> pressed = null)
            where T : UIElement
        {
            normal?.Invoke(element);

            element.Element.RegisterCallback<PointerLeaveEvent>(pointerEvent =>
            {
                normal?.Invoke(element);
                element.Element.ReleaseMouse();
            }, TrickleDown.TrickleDown);

            if (hover != null)
            {
                element.Element.RegisterCallback<PointerEnterEvent>(pointerEvent =>
                {
                    hover.Invoke(element);
                }, TrickleDown.TrickleDown);
            }

            if (pressed != null)
            {
                element.Element.RegisterCallback<PointerDownEvent>(pointerEvent =>
                {
                    pressed?.Invoke(element);
                }, TrickleDown.TrickleDown);
            }

            element.Element.RegisterCallback<PointerUpEvent>(pointerEvent =>
            {
                if (element.Element.worldBound.Contains(pointerEvent.position))
                {
                    hover?.Invoke(element);
                }
                else
                {
                    normal?.Invoke(element);
                }
            }, TrickleDown.TrickleDown);

            return element;
        }

        public static T RegisterFocusEvent<T>(this T element, Action<T, FocusEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterFocusInEvent<T>(this T element, Action<T, FocusInEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterFocusOutEvent<T>(this T element, Action<T, FocusOutEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterBlurEvent<T>(this T element, Action<T, BlurEvent> action) where T : UIElement
        {
            return element.RegisterCallback(action);
        }

        public static T RegisterFocusEvents<T>(this T element,
            Action<T, FocusInEvent> focusIn = null,
            Action<T, FocusOutEvent> focusOut = null)
            where T : UIElement
        {
            RegisterFocusInEvent(element, focusIn);
            RegisterFocusOutEvent(element, focusOut);
            return element;
        }

        public static T SetSlice<T>(this T element, StyleInt slice) where T : UIElement
        {
            element.Element.style.unitySliceTop = slice;
            element.Element.style.unitySliceLeft = slice;
            element.Element.style.unitySliceBottom = slice;
            element.Element.style.unitySliceRight = slice;
            return element;
        }

        public static T SetSlice<T>(
            this T element,
            StyleInt? top = null,
            StyleInt? left = null,
            StyleInt? bottom = null,
            StyleInt? right = null) where T : UIElement
        {
            if (top.HasValue)
            {
                element.Element.style.unitySliceTop = top.Value;
            }

            if (left.HasValue)
            {
                element.Element.style.unitySliceLeft = left.Value;
            }

            if (bottom.HasValue)
            {
                element.Element.style.unitySliceBottom = bottom.Value;
            }

            if (right.HasValue)
            {
                element.Element.style.unitySliceRight = right.Value;
            }

            return element;
        }

        public static T SetBorderColor<T>(this T element, StyleColor value) where T : UIElement
        {
            element.Element.style.borderBottomColor = value;
            element.Element.style.borderLeftColor = value;
            element.Element.style.borderRightColor = value;
            element.Element.style.borderTopColor = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.borderBottomColor" />
        public static T SetBorderBottomColor<T>(this T element, StyleColor value) where T : UIElement
        {
            element.Element.style.borderBottomColor = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.borderLeftColor" />
        public static T SetBorderLeftColor<T>(this T element, StyleColor value) where T : UIElement
        {
            element.Element.style.borderLeftColor = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.borderRightColor" />
        public static T SetBorderRightColor<T>(this T element, StyleColor value) where T : UIElement
        {
            element.Element.style.borderRightColor = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.borderTopColor" />
        public static T SetBorderTopColor<T>(this T element, StyleColor value) where T : UIElement
        {
            element.Element.style.borderTopColor = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.borderRightWidth" />
        public static T SetBorderRightWidth<T>(this T element, StyleFloat value) where T : UIElement
        {
            element.Element.style.borderRightWidth = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.borderTopWidth" />
        public static T SetBorderTopWidth<T>(this T element, StyleFloat value) where T : UIElement
        {
            element.Element.style.borderTopWidth = value;
            return element;
        }

        public static T SetBorderWidth<T>(this T element, StyleFloat value) where T : UIElement
        {
            element.Element.style.borderLeftWidth = value;
            element.Element.style.borderBottomWidth = value;
            element.Element.style.borderRightWidth = value;
            element.Element.style.borderTopWidth = value;
            return element;
        }

        public static T SetBorderWidth<T>(
            this T element,
            StyleFloat? top = null,
            StyleFloat? left = null,
            StyleFloat? bottom = null,
            StyleFloat? right = null) where T : UIElement
        {
            if (top.HasValue)
            {
                element.Element.style.borderTopWidth = top.Value;
            }

            if (left.HasValue)
            {
                element.Element.style.borderLeftWidth = left.Value;
            }

            if (bottom.HasValue)
            {
                element.Element.style.borderBottomWidth = bottom.Value;
            }

            if (right.HasValue)
            {
                element.Element.style.borderRightWidth = right.Value;
            }

            return element;
        }

        /// <inheritdoc cref="IStyle.borderLeftWidth" />
        public static T SetBorderLeftWidth<T>(this T element, StyleFloat value) where T : UIElement
        {
            element.Element.style.borderLeftWidth = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.borderBottomWidth" />
        public static T SetBorderBottomWidth<T>(this T element, StyleFloat value) where T : UIElement
        {
            element.Element.style.borderBottomWidth = value;
            return element;
        }

        public static T SetBorderRadius<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.borderBottomLeftRadius = value;
            element.Element.style.borderBottomRightRadius = value;
            element.Element.style.borderTopLeftRadius = value;
            element.Element.style.borderTopRightRadius = value;
            return element;
        }

        public static T SetBorderRadius<T>(this T element,
            StyleLength? topLeft = null,
            StyleLength? topRight = null,
            StyleLength? bottomLeft = null,
            StyleLength? bomttomRight = null) where T : UIElement
        {
            if (topLeft.HasValue)
            {
                element.Element.style.borderTopLeftRadius = topLeft.Value;
            }

            if (topRight.HasValue)
            {
                element.Element.style.borderTopRightRadius = topRight.Value;
            }

            if (bottomLeft.HasValue)
            {
                element.Element.style.borderBottomLeftRadius = bottomLeft.Value;
            }

            if (bomttomRight.HasValue)
            {
                element.Element.style.borderBottomRightRadius = bomttomRight.Value;
            }

            return element;
        }

        /// <inheritdoc cref="IStyle.borderBottomLeftRadius" />
        public static T SetBorderBottomLeftRadius<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.borderBottomLeftRadius = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.borderBottomRightRadius" />
        public static T SetBorderBottomRightRadius<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.borderBottomRightRadius = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.borderTopLeftRadius" />
        public static T SetBorderTopLeftRadius<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.borderTopLeftRadius = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.borderTopRightRadius" />
        public static T SetBorderTopRightRadius<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.borderTopRightRadius = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.flexShrink" />
        public static T SetFlexShrink<T>(this T element, StyleFloat value) where T : UIElement
        {
            element.Element.style.flexShrink = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.flexGrow" />
        public static T SetFlexGrow<T>(this T element, StyleFloat value) where T : UIElement
        {
            element.Element.style.flexGrow = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.flexDirection" />
        public static T SetFlexDirection<T>(this T element, FlexDirection value) where T : UIElement
        {
            element.Element.style.flexDirection = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.flexWrap" />
        public static T SetFlexWrap<T>(this T element, Wrap value) where T : UIElement
        {
            element.Element.style.flexWrap = value;
            return element;
        }

        /// <inheritdoc cref="IStyle.flexBasis" />
        public static T SetFlexBasis<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.flexBasis = value;
            return element;
        }

        public static T SetVertical<T>(this T element) where T : UIElement
        {
            element.Element.style.flexDirection = FlexDirection.Column;
            return element;
        }

        public static T SetHorizontal<T>(this T element) where T : UIElement
        {
            element.Element.style.flexDirection = FlexDirection.Row;
            return element;
        }

        public static T SetFont<T>(this T element, Font font) where T : UIElement
        {
#if UNITY_2021_3_OR_NEWER
            element.Element.style.unityFontDefinition = FontDefinition.FromFont(font);
#else
            element.Element.style.unityFont = font;
#endif
            return element;
        }

        public static T SetFontSize<T>(this T element, StyleLength fontSize) where T : UIElement
        {
            element.Element.style.fontSize = fontSize;
            return element;
        }

        public static T SetColor<T>(this T element, StyleColor color) where T : UIElement
        {
            element.Element.style.color = color;
            return element;
        }

        public static T SetOverflow<T>(this T element, Overflow overflow) where T : UIElement
        {
            element.Element.style.overflow = overflow;
            return element;
        }

        public static T SetFontStyle<T>(this T element, FontStyle fontStyle) where T : UIElement
        {
            element.Element.style.unityFontStyleAndWeight = fontStyle;
            return element;
        }

        public static T SetFontStyleBold<T>(this T element) where T : UIElement
        {
            element.Element.style.unityFontStyleAndWeight = FontStyle.Bold;
            return element;
        }

        public static T SetFontStyleItalic<T>(this T element) where T : UIElement
        {
            element.Element.style.unityFontStyleAndWeight = FontStyle.Italic;
            return element;
        }

        public static T SetFontStyleBoldAndItalic<T>(this T element) where T : UIElement
        {
            element.Element.style.unityFontStyleAndWeight = FontStyle.BoldAndItalic;
            return element;
        }

        public static T SetTextAlign<T>(this T element, TextAnchor value) where T : UIElement
        {
            element.Element.style.unityTextAlign = value;
            return element;
        }

        public static T SetTextOverflow<T>(this T element, StyleEnum<TextOverflow> value) where T : UIElement
        {
            element.Element.style.textOverflow = value;
            return element;
        }

        public static T SetTextOverflowPosition<T>(this T element, StyleEnum<TextOverflowPosition> value) where T : UIElement
        {
            element.Element.style.unityTextOverflowPosition = value;
            return element;
        }

        public static T SetDistance<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.top = value;
            element.Element.style.right = value;
            element.Element.style.left = value;
            element.Element.style.bottom = value;
            return element;
        }

        public static T SetTop<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.top = value;
            return element;
        }

        public static T SetBottom<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.right = value;
            return element;
        }

        public static T SetRight<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.bottom = value;
            return element;
        }

        public static T SetLeft<T>(this T element, StyleLength value) where T : UIElement
        {
            element.Element.style.left = value;
            return element;
        }

        public static T SetPosition<T>(
            this T element,
            Position? position,
            StyleLength? top = null,
            StyleLength? left = null,
            StyleLength? bottom = null,
            StyleLength? right = null) where T : UIElement
        {
            if (position.HasValue)
            {
                element.Element.style.position = position.Value;
            }

            if (top.HasValue)
            {
                element.Element.style.top = top.Value;
            }

            if (left.HasValue)
            {
                element.Element.style.left = left.Value;
            }

            if (bottom.HasValue)
            {
                element.Element.style.bottom = bottom.Value;
            }

            if (right.HasValue)
            {
                element.Element.style.right = right.Value;
            }

            return element;
        }

        public static T Expand<T>(this T element) where T : UIElement
        {
            element.Element.style.flexShrink = 1;
            element.Element.style.flexGrow = 1;
            return element;
        }

        /// <inheritdoc cref="IStyle.marginTop" />
        public static T SetMarginTop<T>(this T element, StyleLength margin) where T : UIElement
        {
            element.Element.style.marginTop = margin;
            return element;
        }

        /// <inheritdoc cref="IStyle.marginBottom" />
        public static T SetMarginBottom<T>(this T element, StyleLength margin) where T : UIElement
        {
            element.Element.style.marginBottom = margin;
            return element;
        }

        /// <inheritdoc cref="IStyle.marginLeft" />
        public static T SetMarginLeft<T>(this T element, StyleLength margin) where T : UIElement
        {
            element.Element.style.marginLeft = margin;
            return element;
        }

        /// <inheritdoc cref="IStyle.marginRight" />
        public static T SetMarginRight<T>(this T element, StyleLength margin) where T : UIElement
        {
            element.Element.style.marginRight = margin;
            return element;
        }

        public static T SetMargin<T>(this T element, StyleLength margin) where T : UIElement
        {
            element.Element.style.marginTop = margin;
            element.Element.style.marginLeft = margin;
            element.Element.style.marginBottom = margin;
            element.Element.style.marginRight = margin;
            return element;
        }

        public static T SetMargin<T>(
            this T element,
            StyleLength? top = null,
            StyleLength? left = null,
            StyleLength? bottom = null,
            StyleLength? right = null) where T : UIElement
        {
            if (top.HasValue)
            {
                element.Element.style.marginTop = top.Value;
            }

            if (left.HasValue)
            {
                element.Element.style.marginLeft = left.Value;
            }

            if (bottom.HasValue)
            {
                element.Element.style.marginBottom = bottom.Value;
            }

            if (right.HasValue)
            {
                element.Element.style.marginRight = right.Value;
            }

            return element;
        }

        /// <inheritdoc cref="IStyle.paddingTop" />
        public static T SetPaddingTop<T>(this T element, StyleLength padding) where T : UIElement
        {
            element.Element.style.paddingTop = padding;
            return element;
        }

        /// <inheritdoc cref="IStyle.paddingBottom" />
        public static T SetPaddingBottom<T>(this T element, StyleLength padding) where T : UIElement
        {
            element.Element.style.paddingBottom = padding;
            return element;
        }

        /// <inheritdoc cref="IStyle.paddingLeft" />
        public static T SetPaddingLeft<T>(this T element, StyleLength padding) where T : UIElement
        {
            element.Element.style.paddingLeft = padding;
            return element;
        }

        /// <inheritdoc cref="IStyle.paddingRight" />
        public static T SetPaddingRight<T>(this T element, StyleLength padding) where T : UIElement
        {
            element.Element.style.paddingRight = padding;
            return element;
        }

        public static T SetPadding<T>(this T element, StyleLength padding) where T : UIElement
        {
            element.Element.style.paddingTop = padding;
            element.Element.style.paddingLeft = padding;
            element.Element.style.paddingBottom = padding;
            element.Element.style.paddingRight = padding;
            return element;
        }

        public static T SetPadding<T>(
            this T element,
            StyleLength? top = null,
            StyleLength? left = null,
            StyleLength? bottom = null,
            StyleLength? right = null) where T : UIElement
        {
            if (top.HasValue)
            {
                element.Element.style.paddingTop = top.Value;
            }

            if (left.HasValue)
            {
                element.Element.style.paddingLeft = left.Value;
            }

            if (bottom.HasValue)
            {
                element.Element.style.paddingBottom = bottom.Value;
            }

            if (right.HasValue)
            {
                element.Element.style.paddingRight = right.Value;
            }

            return element;
        }

        /// <inheritdoc cref="VisualElement.visible" />
        public static T SetVisible<T>(this T element, bool visible) where T : UIElement
        {
            element.Element.visible = visible;
            return element;
        }

        /// <inheritdoc cref="VisualElement.SetEnabled" />
        public static T SetEnabled<T>(this T element, bool enabled = true) where T : UIElement
        {
            element.Element.SetEnabled(enabled);
            return element;
        }

        public static T SetDisabled<T>(this T element, bool disabled = true) where T : UIElement
        {
            element.Element.SetEnabled(!disabled);
            return element;
        }

        /// <inheritdoc cref="IStyle.display" />
        public static T SetDisplay<T>(this T element, DisplayStyle value) where T : UIElement
        {
            element.Element.style.display = value;
            return element;
        }

        public static T BindVisibility<T>(this T element, IUIBinding<bool> binding, bool hideLayout = false) where T : UIElement
        {
            if (hideLayout)
            {
                element.Element.style.display = binding.Value ? DisplayStyle.Flex : DisplayStyle.None;
                binding.Changed += changed => element.Element.style.display = binding.Value ? DisplayStyle.Flex : DisplayStyle.None;
                return element;
            }
            else
            {
                element.Element.style.visibility = binding.Value ? Visibility.Visible : Visibility.Hidden;
                binding.Changed += changed => element.Element.style.visibility = binding.Value ? Visibility.Visible : Visibility.Hidden;
                return element;
            }
        }

        public static T BindEnable<T>(this T element, IUIBinding<bool> binding) where T : UIElement
        {
            return BindingUtils.Bind(element, element.Element.SetEnabled, binding);
        }

#if UNITY_2021_3_OR_NEWER
        public static T SetTextShadow<T>(this T element, StyleTextShadow value) where T : UIElement
        {
            element.Element.style.textShadow = value;
            return element;
        }

        public static T SetTextOutlineColor<T>(this T element, StyleColor value) where T : UIElement
        {
            element.Element.style.unityTextOutlineColor = value;
            return element;
        }

        public static T SetTextOutlineWidth<T>(this T element, StyleFloat value) where T : UIElement
        {
            element.Element.style.unityTextOutlineWidth = value;
            return element;
        }

#endif
    }

    static class BindingUtils
    {
        public static T Bind<T, U>(this T element, Action<U> setter, IUIBinding<U> binding)
            where T : UIElement
        {
            setter(binding.Value);
            binding.Changed += changed => setter(changed);
            return element;
        }

        public static T Bind<T, U>(this T element, Action<U> setter, Action<Action<U>> registerCallback, IUIBinding<U> binding)
            where T : UIElement
        {
            setter(binding.Value);
            registerCallback(value => binding.Value = value);
            binding.Changed += changed => setter(changed);
            return element;
        }

        public static T Bind<T, U>(T element, Action<U> setter, Func<EventCallback<ChangeEvent<U>>, bool> callbackRegistration, IUIBinding<U> binding)
            where T : UIElement
        {
            setter(binding.Value);
            callbackRegistration(value => binding.Value = value.newValue);
            binding.Changed += changed => setter(changed);
            return element;
        }

        public static T BindText<T, U>(T element, IUIBinding<string> binding)
            where T : UIElement<U>
            where U : TextElement, new()
        {
            return Bind(element, value => element.Element.text = value, element.Element.RegisterValueChangedCallback, binding);
        }

        public static T BindValue<T, U, V>(T element, IUIBinding<V> binding)
            where T : UIElement<U>
            where U : BaseField<V>, new()
        {
            return Bind(element, value => element.Element.value = value, element.Element.RegisterValueChangedCallback, binding);
        }
    }

    internal static class UILabelExtensions
    {
        public static T SetText<T>(this T element, string text) where T : UILabel
        {
            element.Element.text = text;
            return element;
        }

        public static T BindValue<T>(this T element, IUIBinding<string> binding) where T : UILabel
        {
            return BindingUtils.BindText<T, Label>(element, binding);
        }

        public static T Wrap<T>(this T element) where T : UILabel
        {
            element.SetWhitespace(WhiteSpace.Normal);
            return element;
        }

        public static T NoWrap<T>(this T element) where T : UILabel
        {
            element.SetWhitespace(WhiteSpace.NoWrap);
            return element;
        }
    }

    internal static class UIFoldoutExtensions
    {
        public static T SetText<T>(this T element, string text) where T : UIFoldout
        {
            element.Element.text = text;
            return element;
        }

        public static T RegisterValueChanged<T>(this T element, EventCallback<ChangeEvent<bool>> callback) where T : UIFoldout
        {
            element.Element.RegisterValueChangedCallback(callback);
            return element;
        }

        public static T UnregisterValueChanged<T>(this T element, EventCallback<ChangeEvent<bool>> callback) where T : UIFoldout
        {
            element.Element.UnregisterValueChangedCallback(callback);
            return element;
        }

        public static T BindValue<T>(this T element, IUIBinding<bool> binding) where T : UIFoldout
        {
            if (binding == null)
            {
                Debug.LogError($"Cannot bind value. Binding is null.");
                return element;
            }

            element.Element.value = binding.Value;
            element.RegisterValueChanged(value => binding.Value = value.newValue);
            binding.Changed += changed => element.Element.SetValueWithoutNotify(changed);
            return element;
        }
    }

    internal static class UIToggleExtensions
    {
        public static T SetText<T>(this T element, string text) where T : UIToggle
        {
            element.Element.text = text;
            return element;
        }

        public static T SetValue<T>(this T element, bool value) where T : UIToggle
        {
            element.Element.value = value;
            return element;
        }

        public static T RegisterValueChanged<T>(this T element, EventCallback<ChangeEvent<bool>> callback) where T : UIToggle
        {
            element.Element.RegisterValueChangedCallback(callback);
            return element;
        }

        public static T UnregisterValueChanged<T>(this T element, EventCallback<ChangeEvent<bool>> callback) where T : UIToggle
        {
            element.Element.UnregisterValueChangedCallback(callback);
            return element;
        }

        public static T BindValue<T>(this T element, IUIBinding<bool> binding) where T : UIToggle
        {
            return BindingUtils.BindValue<T, Toggle, bool>(element, binding);
        }
    }

    internal static class UIButtonExtensions
    {
        public static T SetText<T>(this T element, string text) where T : UIButton
        {
            element.Element.text = text;
            return element;
        }

#if UNITY_2023_2_OR_NEWER
        public static T SetImage<T>(this T element, Texture2D image) where T : UIButton
        {
            element.Element.iconImage = image;
            return element;
        }

#endif

        public static T SetClickCallback<T>(this T element, Action action) where T : UIButton
        {
            element.Element.clicked += action;
            return element;
        }

        public static T SetClickCallback<T>(this T element, Func<Task> action) where T : UIButton
        {
            element.Element.clicked += () => action();
            return element;
        }
    }

    internal static class UIImageExtensions
    {
        public static T SetImage<T>(this T element, Texture2D image) where T : UIImage
        {
            element.Element.image = image;
            return element;
        }

#if UNITY_2021_3_OR_NEWER
        public static T SetSprite<T>(this T element, Sprite sprite) where T : UIImage
        {
            element.Element.sprite = sprite;
            return element;
        }

#endif

        public static T BindImage<T>(this T element, IUIBinding<Texture2D> binding) where T : UIImage
        {
            element.Element.image = binding.Value;
            binding.Changed += changed => element.Element.image = binding.Value;
            return element;
        }
    }
    #endregion

    #region Scope
    /// <summary>
    /// Scope for setting the current element during creation
    /// </summary>
    class UIScope : IDisposable
    {
        public event Action<UIScope> Release;

        public readonly UIElement Element;

        internal UIScope(UIElement element)
        {
            Element = element;
        }

        /// <summary>
        /// Release the scope
        /// </summary>
        public void Dispose()
        {
            Release?.Invoke(this);
        }
    }
    #endregion

    #region Animations
    interface IUIAnimation
    {
        public UIElement Element { get; set; }
        public void Update(float deltaTime);
    }
    #endregion

    #region Bindings
    interface IUIBinding
    {
        public void Update();
    }

    interface IUIBinding<T> : IUIBinding
    {
        public event Action<T> Changed;

        public T Value { get; set; }
    }

    class TargetUIBinding<T> : IUIBinding<T>
    {
        public static implicit operator T(TargetUIBinding<T> d) => d.Value;

        public event Action<T> Changed;

        public T Value
        {
            get
            {
                if (m_Getter != null)
                {
                    return m_Getter();
                }

                return default;
            }
            set
            {
                SetValue(value);
            }
        }

        T LastValue;

        Action<T> m_Setter;
        Func<T> m_Getter;

        public TargetUIBinding(Func<T> getter, Action<T> setter, Action<T> callback = null)
        {
            m_Getter = getter;
            m_Setter = setter;

            if (callback != null)
            {
                Changed += callback;
            }

            LastValue = getter();
        }

        void SetValue(T value)
        {
            if (m_Setter != null)
            {
                m_Setter(value);
            }

            if (!LastValue?.Equals(Value) ?? false)
            {
                LastValue = Value;
                Changed?.Invoke(Value);
            }
        }

        public void Update()
        {
            if (Value != null && !Value.Equals(LastValue))
            {
                LastValue = Value;
                Changed?.Invoke(Value);
            }
        }
    }

    class ValueUIBinding<T> : IUIBinding<T>
    {
        public static implicit operator T(ValueUIBinding<T> d) => d.Value;

        public event Action<T> Changed;

        public T Value { get => m_Value; set => SetValue(value); }

        T m_Value;

        public ValueUIBinding(T value = default, Action<T> callback = null)
        {
            m_Value = value;
            Changed += callback;
        }

        void SetValue(T value)
        {
            m_Value = value;
            Changed?.Invoke(m_Value);
        }

        public void Update()
        {
        }
    }
    #endregion

    #region Editor Service
    /// <summary>
    /// Service providing fluent facade to UI elements.
    /// Includes both editor and runtime elements.
    /// </summary>
    class UIEditorService : UIService
    {
        double? timeSinceStartup;

        public UIEditorService()
        {
        }

        protected override void UpdateAnimations()
        {
            if (timeSinceStartup == null)
            {
                timeSinceStartup = EditorApplication.timeSinceStartup;
                return;
            }

            var deltaTime = EditorApplication.timeSinceStartup - timeSinceStartup;
            timeSinceStartup = EditorApplication.timeSinceStartup;

            foreach (var animation in Animations)
            {
                try
                {
                    animation.Update((float)deltaTime);
                }
                catch (Exception)
                {
                    Debug.Log("Animation update exception");
                }
            }
        }

        public override UISnippet Snippet()
        {
            return base.Snippet();
        }

        public UIColorField ColorField()
        {
            return Add(new UIColorField(this));
        }

        public UICurveField CurveField()
        {
            return Add(new UICurveField(this));
        }

        public UIEnumFlagsField EnumFlagsField()
        {
            return Add(new UIEnumFlagsField(this));
        }

        public UIGradientField GradientField()
        {
            return Add(new UIGradientField(this));
        }

        public UIInspectorElement InspectorElement()
        {
            return Add(new UIInspectorElement(this));
        }

        public UIInspectorElement InspectorElement(UnityEngine.Object obj)
        {
            return Add(new UIInspectorElement(this, obj));
        }

        public UIInspectorElement InspectorElement(SerializedObject obj)
        {
            return Add(new UIInspectorElement(this, obj));
        }

        public UILayerField LayerField()
        {
            return Add(new UILayerField(this));
        }

        public UILayerMaskField LayerMaskField()
        {
            return Add(new UILayerMaskField(this));
        }

        public UIMaskField MaskField()
        {
            return Add(new UIMaskField(this));
        }

        public UIObjectField ObjectField()
        {
            return Add(new UIObjectField(this));
        }

        public UIPopupField<T> PopupField<T>(string label = null)
        {
            return Add(new UIPopupField<T>(this, new PopupField<T>(label)));
        }

        public UIPopupField<T> PopupField<T>(List<T> choices, T defaultValue, Func<T, string> formatSelectedValueCallback = null, Func<T, string> formatListItemCallback = null)
        {
            return Add(new UIPopupField<T>(this, new PopupField<T>(choices, defaultValue, formatSelectedValueCallback, formatListItemCallback)));
        }

        public UIPopupField<T> PopupField<T>(List<T> choices, int defaultIndex = 0, Func<T, string> formatSelectedValueCallback = null, Func<T, string> formatListItemCallback = null)
        {
            return Add(new UIPopupField<T>(this, new PopupField<T>(choices, defaultIndex, formatSelectedValueCallback, formatListItemCallback)));
        }

        public UIPopupField<T> PopupField<T>(string label, List<T> choices, T defaultValue, Func<T, string> formatSelectedValueCallback = null, Func<T, string> formatListItemCallback = null)
        {
            return Add(new UIPopupField<T>(this, new PopupField<T>(label, choices, defaultValue, formatSelectedValueCallback, formatListItemCallback)));
        }

        public UIPopupField<T> PopupField<T>(string label, List<T> choices, int defaultIndex = 0, Func<T, string> formatSelectedValueCallback = null, Func<T, string> formatListItemCallback = null)
        {
            return Add(new UIPopupField<T>(this, new PopupField<T>(label, choices, defaultIndex, formatSelectedValueCallback, formatListItemCallback)));
        }

        public UIPropertyField PropertyField()
        {
            return Add(new UIPropertyField(this));
        }

        public UITagField TagField()
        {
            return Add(new UITagField(this));
        }

        public UIToolbar Toolbar()
        {
            return Add(new UIToolbar(this));
        }

        public UIToolbarBreadcrumbs ToolbarBreadcrumbs()
        {
            return Add(new UIToolbarBreadcrumbs(this));
        }

        public UIToolbarButton ToolbarButton()
        {
            return Add(new UIToolbarButton(this));
        }

        public UIToolbarMenu ToolbarMenu()
        {
            return Add(new UIToolbarMenu(this));
        }

        public UIToolbarPopupSearchField ToolbarPopupSearchField()
        {
            return Add(new UIToolbarPopupSearchField(this));
        }

        public UIToolbarSearchField ToolbarSearchField()
        {
            return Add(new UIToolbarSearchField(this));
        }

        public UIToolbarSpacer ToolbarSpacer()
        {
            return Add(new UIToolbarSpacer(this));
        }

        public UIToolbarToggle ToolbarToggle()
        {
            return Add(new UIToolbarToggle(this));
        }
    }
    #endregion

    #region Editor Elements
    class UIEditorElement<T> : UIElement<T> where T : VisualElement, new()
    {
        protected new UIEditorService UI { get; }

        internal UIEditorElement(UIEditorService ui) : base(ui)
        {
            UI = ui;
        }

        internal UIEditorElement(UIEditorService ui, T value) : base(ui, value)
        {
            UI = ui;
        }
    }

    class UIColorField : UIEditorElement<ColorField>
    {
        internal UIColorField(UIEditorService ui) : base(ui) {}
        internal UIColorField(UIEditorService ui, ColorField element) : base(ui, element) {}
    }

    class UICurveField : UIEditorElement<CurveField>
    {
        internal UICurveField(UIEditorService ui) : base(ui) {}
        internal UICurveField(UIEditorService ui, CurveField element) : base(ui, element) {}
    }

    class UIEnumFlagsField : UIEditorElement<EnumFlagsField>
    {
        internal UIEnumFlagsField(UIEditorService ui) : base(ui) {}
        internal UIEnumFlagsField(UIEditorService ui, EnumFlagsField element) : base(ui, element) {}
    }

    class UIGradientField : UIEditorElement<GradientField>
    {
        internal UIGradientField(UIEditorService ui) : base(ui) {}
        internal UIGradientField(UIEditorService ui, GradientField element) : base(ui, element) {}
    }

    class UIInspectorElement : UIEditorElement<InspectorElement>
    {
        internal UIInspectorElement(UIEditorService ui) : base(ui) {}
        internal UIInspectorElement(UIEditorService ui, UnityEngine.Object obj) : base(ui, new InspectorElement(obj)) {}
        internal UIInspectorElement(UIEditorService ui, SerializedObject obj) : base(ui, new InspectorElement(obj)) {}
    }

    class UIObjectField : UIEditorElement<ObjectField>
    {
        internal UIObjectField(UIEditorService ui) : base(ui) {}
        internal UIObjectField(UIEditorService ui, ObjectField element) : base(ui, element) {}
    }

    class UIPopupField<T> : UIEditorElement<PopupField<T>>
    {
        internal UIPopupField(UIEditorService ui) : base(ui) {}
        internal UIPopupField(UIEditorService ui, PopupField<T> element) : base(ui, element) {}

        public UIPopupField<T> RegisterValueChanged(EventCallback<ChangeEvent<T>> callback)
        {
            Element.RegisterValueChangedCallback(callback);
            return this;
        }

        public UIPopupField<T> UnregisterValueChanged(EventCallback<ChangeEvent<T>> callback)
        {
            Element.UnregisterValueChangedCallback(callback);
            return this;
        }
    }

    class UIPropertyField : UIEditorElement<PropertyField>
    {
        internal UIPropertyField(UIEditorService ui) : base(ui) {}
        internal UIPropertyField(UIEditorService ui, PropertyField element) : base(ui, element) {}
    }

    class UITagField : UIEditorElement<TagField>
    {
        internal UITagField(UIEditorService ui) : base(ui) {}
        internal UITagField(UIEditorService ui, TagField element) : base(ui, element) {}
    }

    class UILayerField : UIEditorElement<LayerField>
    {
        internal UILayerField(UIEditorService ui) : base(ui) {}
        internal UILayerField(UIEditorService ui, LayerField element) : base(ui, element) {}
    }

    class UILayerMaskField : UIEditorElement<LayerMaskField>
    {
        internal UILayerMaskField(UIEditorService ui) : base(ui) {}
        internal UILayerMaskField(UIEditorService ui, LayerMaskField element) : base(ui, element) {}
    }

    class UIMaskField : UIEditorElement<MaskField>
    {
        internal UIMaskField(UIEditorService ui) : base(ui) {}
        internal UIMaskField(UIEditorService ui, MaskField element) : base(ui, element) {}
    }

    class UIToolbar : UIEditorElement<Toolbar>
    {
        internal UIToolbar(UIEditorService ui) : base(ui) {}
        internal UIToolbar(UIEditorService ui, Toolbar element) : base(ui, element) {}
    }

    class UIToolbarBreadcrumbs : UIEditorElement<ToolbarBreadcrumbs>
    {
        internal UIToolbarBreadcrumbs(UIEditorService ui) : base(ui) {}
        internal UIToolbarBreadcrumbs(UIEditorService ui, ToolbarBreadcrumbs element) : base(ui, element) {}
    }

    class UIToolbarButton : UIEditorElement<ToolbarButton>
    {
        internal UIToolbarButton(UIEditorService ui) : base(ui) {}
        internal UIToolbarButton(UIEditorService ui, ToolbarButton element) : base(ui, element) {}
    }

    class UIToolbarMenu : UIEditorElement<ToolbarMenu>
    {
        internal UIToolbarMenu(UIEditorService ui) : base(ui) {}
        internal UIToolbarMenu(UIEditorService ui, ToolbarMenu element) : base(ui, element) {}
    }

    class UIToolbarPopupSearchField : UIEditorElement<ToolbarPopupSearchField>
    {
        internal UIToolbarPopupSearchField(UIEditorService ui) : base(ui) {}
        internal UIToolbarPopupSearchField(UIEditorService ui, ToolbarPopupSearchField element) : base(ui, element) {}
    }

    class UIToolbarSearchField : UIEditorElement<ToolbarSearchField>
    {
        internal UIToolbarSearchField(UIEditorService ui) : base(ui) {}
        internal UIToolbarSearchField(UIEditorService ui, ToolbarSearchField element) : base(ui, element) {}
    }

    class UIToolbarSpacer : UIEditorElement<ToolbarSpacer>
    {
        internal UIToolbarSpacer(UIEditorService ui) : base(ui) {}
        internal UIToolbarSpacer(UIEditorService ui, ToolbarSpacer element) : base(ui, element) {}
    }

    class UIToolbarToggle : UIEditorElement<ToolbarToggle>
    {
        internal UIToolbarToggle(UIEditorService ui) : base(ui) {}
        internal UIToolbarToggle(UIEditorService ui, ToolbarToggle element) : base(ui, element) {}
    }
    #endregion

    #region Editor Extensions
    internal static class UIPopupFieldExtensions
    {
        public static T BindValue<T, U>(this T element, IUIBinding<U> binding) where T : UIPopupField<U>
        {
            return BindingUtils.BindValue<T, PopupField<U>, U>(element, binding);
        }
    }

    internal static class UIToolbarButtonExtensions
    {
        public static T SetText<T>(this T element, string text) where T : UIToolbarButton
        {
            element.Element.text = text;
            return element;
        }

        public static T SetClickCallback<T>(this T element, Action action) where T : UIToolbarButton
        {
            element.Element.clicked += action;
            return element;
        }

        public static T SetClickCallback<T>(this T element, Func<Task> action) where T : UIToolbarButton
        {
            element.Element.clicked += () => action();
            return element;
        }
    }
    #endregion
}
