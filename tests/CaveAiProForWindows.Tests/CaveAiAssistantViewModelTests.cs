using CaveAiProForWindows.Models;
using CaveAiProForWindows.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class CaveAiAssistantViewModelTests
{
    [TestMethod]
    public void SendMessage_usesOfflineBrain_forSelectedProject()
    {
        var vm = new CaveAiAssistantViewModel();
        var project = new CaveProjectDocument { Name = "Test Cave" };
        vm.Attach(() => project, () => Array.Empty<KnownCaveRecord>());
        vm.PanelVisible = true;
        vm.InputText = "what is the max depth?";
        vm.SendMessageCommand.Execute(null);
        Assert.IsTrue(vm.Messages.Count >= 2);
        var reply = vm.Messages.Last(m => !m.IsUser);
        Assert.AreEqual(CaveAiAssistantReplySource.Device, reply.ReplySource);
        Assert.IsFalse(reply.Text.Contains("Gemini", StringComparison.OrdinalIgnoreCase));
    }
}