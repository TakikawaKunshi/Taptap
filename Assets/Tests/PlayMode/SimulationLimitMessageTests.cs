using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class SimulationLimitMessageTests
{
    private const string Notice = "该生态箱已达到模拟上限，感谢游玩喵";
    private GameObject root;
    private TimeManager time;
    private Button button;
    private NightSettleButton control;
    private MessagePanel panel;
    private RectTransform information;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Simulation limit test", typeof(RectTransform), typeof(Canvas));
        root.SetActive(false);
        GameObject panelObject = Child("MessagePanel", root.transform);
        GameObject bar = Child("MessageBar", panelObject.transform);
        GameObject contentPanel = Child("ContentPanel", panelObject.transform);
        foreach (MessageTag tag in System.Enum.GetValues(typeof(MessageTag)))
        {
            Child(tag + "Btn", bar.transform).AddComponent<Button>();
            GameObject scrollObject = Child(tag + "SV", contentPanel.transform);
            ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.viewport = Child("Viewport", scrollObject.transform).GetComponent<RectTransform>();
            scroll.content = Child("Content", scroll.viewport).GetComponent<RectTransform>();
            if (tag == MessageTag.Information) information = scroll.content;
        }
        panel = panelObject.AddComponent<MessagePanel>();
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("Fonts/STXINWEI SDF");
        if (font != null) Set(panel, "messageFont", font);

        GameObject manager = Child("Time", root.transform);
        WeatherBase weather = manager.AddComponent<WeatherBase>();
        NightSettlement night = manager.AddComponent<NightSettlement>();
        Set(night, "weather", weather);
        Set(night, "settlementDelay", 0f);
        time = manager.AddComponent<TimeManager>();
        Set(time, "maxDays", 2);
        Set(time, "nightSettlement", night);
        GameObject buttonObject = Child("Night button", root.transform);
        button = buttonObject.AddComponent<Button>();
        control = buttonObject.AddComponent<NightSettleButton>();
        Set(control, "timeManager", time);
        // 不手动绑定面板，同时验证现有场景会自动找到同 Canvas 中的 InformationSV。
        root.SetActive(true);
        panel.ShowInformation("原有消息");
        panel.SelectTab(MessageTag.Daily);
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(root);

    [UnityTest]
    public IEnumerator FinalNightShowsNoticeWithFiveBlankLinesAndFurtherClicksOnlyAddMessages()
    {
        int nights = 0;
        int endings = 0;
        time.NightSettled += _ => nights++;
        time.GameSettlementStarted += _ => endings++;
        button.onClick.Invoke();
        yield return FinishNight();
        Assert.AreEqual(2, time.CurrentDay);
        Assert.AreEqual(1, information.childCount, "到达最后一天时仍保留当天操作，不提前显示结束提示。");

        button.onClick.Invoke();
        Assert.IsFalse(button.interactable);
        Assert.AreEqual(1, information.childCount, "提示应等待最后一晚成功结算。");
        yield return FinishNight();
        Assert.IsTrue(time.IsGameSettlement);
        Assert.IsTrue(button.interactable);
        Assert.AreEqual(MessageTag.Information, panel.SelectedTag);
        Assert.AreEqual(2, information.childCount);
        TMP_Text latest = information.GetChild(0).GetComponent<TMP_Text>();
        Assert.AreEqual(Notice + "\n\n\n\n\n\n", latest.text);
        Assert.AreEqual("原有消息", information.GetChild(1).GetComponent<TMP_Text>().text);

        float plainHeight = latest.GetPreferredValues(Notice, 10000f, 10000f).y;
        float lineHeight = latest.GetPreferredValues("A\nA", 10000f, 10000f).y
            - latest.GetPreferredValues("A", 10000f, 10000f).y;
        float spacedHeight = latest.GetPreferredValues(latest.text, 10000f, 10000f).y;
        Assert.That(spacedHeight - plainHeight, Is.EqualTo(5f * lineHeight).Within(0.1f),
            "五行空白必须实际撑开 TMP 布局，不能只在字符串中存在。");

        control.enabled = false;
        control.enabled = true;
        button.onClick.Invoke();
        Assert.AreEqual(3, information.childCount, "重新启用后每次点击只追加一条。");
        Assert.AreEqual(2, time.CurrentDay);
        Assert.AreEqual(2, nights);
        Assert.AreEqual(1, endings);
        Assert.IsFalse(time.IsSettlementRunning);
        Assert.IsFalse(time.TryStartNightSettlement());
        time.enabled = false;
        button.onClick.Invoke();
        Assert.AreEqual(3, information.childCount, "管理器停用后不接受点击。");
    }

    [UnityTest]
    public IEnumerator InterruptedFinalNightDoesNotShowNoticeUntilRetryCompletes()
    {
        Set(time, "maxDays", 1);
        button.onClick.Invoke();
        time.enabled = false;
        yield return null;
        Assert.AreEqual(1, information.childCount);
        Assert.IsFalse(time.IsGameSettlement);
        time.enabled = true;
        button.onClick.Invoke();
        yield return FinishNight();
        Assert.IsTrue(time.IsGameSettlement);
        Assert.AreEqual(2, information.childCount);
        Assert.AreEqual(Notice + "\n\n\n\n\n\n", information.GetChild(0).GetComponent<TMP_Text>().text);
    }

    private IEnumerator FinishNight()
    {
        for (int frame = 0; frame < 10 && time.IsSettlementRunning; frame++) yield return null;
        Assert.IsFalse(time.IsSettlementRunning);
        yield return null;
    }

    private static GameObject Child(string name, Transform parent)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return child;
    }

    private static void Set(object target, string field, object value)
    {
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
