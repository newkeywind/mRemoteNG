using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using mRemoteNG.Config.UserProfiles;

namespace mRemoteNG.UI.Forms
{
    internal sealed class ProfileSelectionDialog : Form
    {
        private const int MarginPx = 12;

        private readonly ComboBox _profiles = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };

        private readonly Button _accept = new()
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(75, 23),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };

        private ProfileSelectionDialog(IReadOnlyList<ConnectionProfile> profiles)
        {
            Text = "Select connection profile";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;

            // Deterministic, DPI-scaled layout. AutoSize on the composited container is
            // intentionally avoided because a Dock=Fill ComboBox misreports its preferred
            // height under display scaling, which shrinks the container and clips the OK
            // button. The button instead sizes itself to its (scaled) text and is placed
            // explicitly once the form has finished scaling.
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            _profiles.DataSource = profiles.ToList();
            _profiles.DisplayMember = nameof(ConnectionProfile.Name);

            ClientSize = new Size(360, 150);

            Controls.Add(_profiles);
            Controls.Add(_accept);
            AcceptButton = _accept;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            PositionAcceptButton();
        }

        private void PositionAcceptButton()
        {
            _accept.Location = new Point(
                ClientSize.Width - MarginPx - _accept.Width,
                ClientSize.Height - MarginPx - _accept.Height);
        }

        public static ConnectionProfile Select(IReadOnlyList<ConnectionProfile> profiles)
        {
            using ProfileSelectionDialog dialog = new(profiles);
            return dialog.ShowDialog() == DialogResult.OK ? (ConnectionProfile)dialog._profiles.SelectedItem : null;
        }
    }
}
