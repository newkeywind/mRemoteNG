using System.Threading;
using System.Windows.Forms;
using mRemoteNGTests.TestHelpers;
using NUnit.Framework;

namespace mRemoteNGTests.UI.Forms.OptionsPages
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class OptionsStartupExitPageTests : OptionsFormSetupAndTeardown
    {
        [Test]
        public void StartupExitPageLinkExistsInListView()
        {
            ListViewTester listViewTester = new("lstOptionPages", _optionsForm);
            Assert.That(listViewTester.Items[0].Text, Does.Match("Startup/Exit"));
        }

        [Test]
        public void IconShownInListView()
        {
            ListViewTester listViewTester = new("lstOptionPages", _optionsForm);
            Assert.That(listViewTester.Items[0].ImageList, Is.Not.Null);
        }

        [Test]
        public void SelectingStartupExitPageLoadsSettings()
        {
            ListViewTester listViewTester = new("lstOptionPages", _optionsForm);
            listViewTester.Select("Startup/Exit");
            CheckBox checkboxTester = _optionsForm.FindControl<CheckBox>("chkSaveConsOnExit");
            Assert.That(checkboxTester.Text, Does.Match("Save connections"));
        }

        [Test]
        public void ConnectionSourceComboDefaultsToFile()
        {
            ListViewTester listViewTester = new("lstOptionPages", _optionsForm);
            listViewTester.Select("Startup/Exit");
            ComboBox comboBoxTester = _optionsForm.FindControl<ComboBox>("cboConnectionSource");
            Assert.That(comboBoxTester, Is.Not.Null);
            Assert.That(comboBoxTester.Items.Count, Is.EqualTo(2));
            Assert.That(comboBoxTester.SelectedIndex, Is.EqualTo(0));
        }
    }
}