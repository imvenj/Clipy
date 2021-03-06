﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Clipy
{
    public partial class MainForm : Form
    {
        // MFC Import
        [DllImport("User32.dll")]
        protected static extern int SetClipboardViewer(int hWndNewViewer);
        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        public static extern bool ChangeClipboardChain(IntPtr hWndRemove, IntPtr hWndNewNext);
        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        public static extern int SendMessage(IntPtr hwnd, int wMsg, IntPtr wParam, IntPtr lParam);
        // Bring window to front while not in focus.
        [DllImport("User32.dll")]
        public static extern Int32 SetForegroundWindow(int hWnd);
        
        private int MAX_COUNT = Properties.Settings.Default.numberOfHistories;
        private int MAX_MENU_TITLE = Properties.Settings.Default.menuLength;
        private int NUMBER_OF_ITEMS_PER_GROUP = Properties.Settings.Default.itemsPerGroup;

        // For localization.
        private ResourceManager resmgr = new ResourceManager("Clipy.Strings", Assembly.GetExecutingAssembly());
        private CultureInfo ci = Thread.CurrentThread.CurrentUICulture;

        // Local variable
        IntPtr nextClipboardViewer;
        List<Group> groups;
        List<History> histories;
        List<History> currentSnippets;

        // Calculated Property
        private MenuItem settingsMenu;
        private MenuItem SettingsMenu
        {
            get
            {
                if (settingsMenu == null)
                {
                    settingsMenu = new MenuItem();
                    settingsMenu.Text = resmgr.GetString("__menu_title_Settings", ci);
                    settingsMenu.Click += SettingsMenu_Click;
                }
                return settingsMenu;
            }
        }

        private void SettingsMenu_Click(object sender, EventArgs e)
        {
            SettingsPane settingsPane = new SettingsPane();
            settingsPane.ShowDialog();
            // Delay 0.1s to allow settings to sync.
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            timer.Interval = 100;
            timer.Tick += (obj, eventArgs) => {
                timer.Stop();
                UpdateTrayMenu();
                UpdateUIFromSettings();
            };
            timer.Start();
        }
        private MenuItem editMenu;
        private MenuItem EditMenu
        {
            get
            {
                if (editMenu == null)
                {
                    editMenu = new MenuItem();
                    editMenu.Text = resmgr.GetString("__menu_title_Snippets", ci);
                    editMenu.Click += EditMenu_Click;
                }
                return editMenu;
            }
        }

        private void EditMenu_Click(object sender, EventArgs e)
        {
            MakeKeyAndVisiable();
        }

        private MenuItem exitMenu;
        private MenuItem ExitMenu
        {
            get
            {
                if (exitMenu == null)
                {
                    exitMenu = new MenuItem();
                    exitMenu.Text = resmgr.GetString("__menu_title_Exit", ci);
                    exitMenu.Click += ExitMenu_Click;
                }
                return exitMenu;
            }
        }
        
        private void ExitMenu_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private MenuItem deleteHistoriesMenu;
        private MenuItem DeleteHistoriesMenu
        {
            get
            {
                if (deleteHistoriesMenu == null)
                {
                    deleteHistoriesMenu = new MenuItem();
                    deleteHistoriesMenu.Text = resmgr.GetString("__menu_title_Delete_Histories", ci);
                    deleteHistoriesMenu.Click += DeleteHistoriesMenu_Click; ;
                }
                return deleteHistoriesMenu;
            }
        }

        private void DeleteHistoriesMenu_Click(object sender, EventArgs e)
        {
            string message = resmgr.GetString("__message_box_Warn_Delete_Histories", ci);
            string caption = resmgr.GetString("__menu_title_Delete_Histories", ci);
            MessageBoxButtons buttons = MessageBoxButtons.YesNo;
            var result = MessageBox.Show(this, message, caption, buttons, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
            if (result == DialogResult.Yes)
            {
                Clipboard.Clear();
                var db = new DataProcess();
                db.DeleteAllHistories();
                histories = db.LoadHistories();
                UpdateTrayMenu();
            }
        }

        public MainForm()
        {
            InitializeComponent();
            nextClipboardViewer = (IntPtr)SetClipboardViewer((int)this.Handle);
            // Initialize database.
            DataProcess db = new DataProcess();
            ReloadGroupsUI();
            // Initialize Tray Menu.
            UpdateTrayMenu();
            // Hide App When start.
            Hide();
            WindowState = FormWindowState.Minimized;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            UpdateUIFromSettings();
        }
        
        private void UpdateUIFromSettings()
        {
            contentTextBox.Font = fetchMonoFont();
        }

        private Font fetchMonoFont()
        {
            string fontName = Properties.Settings.Default.monoFontName;
            float fontSize = Properties.Settings.Default.monoFontSize;
            FontStyle fontStyle = (FontStyle)Enum.Parse(typeof(FontStyle), Properties.Settings.Default.monoFontStyle);
            var UIFont = new Font(fontName, fontSize, fontStyle);
            return UIFont;
        }

        private void reloadSettings()
        {
            MAX_COUNT = Properties.Settings.Default.numberOfHistories;
            MAX_MENU_TITLE = Properties.Settings.Default.menuLength;
            NUMBER_OF_ITEMS_PER_GROUP = Properties.Settings.Default.itemsPerGroup;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
            }
        }

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            // defined in winuser.h
            const int WM_DRAWCLIPBOARD = 0x0308;
            const int WM_CHANGECBCHAIN = 0x030D;

            switch (m.Msg)
            {
                case WM_DRAWCLIPBOARD:
                    ClipboardDataChanged();
                    SendMessage(nextClipboardViewer, m.Msg, m.WParam, m.LParam);
                    break;

                case WM_CHANGECBCHAIN:
                    if (m.WParam == nextClipboardViewer)
                        nextClipboardViewer = m.LParam;
                    else
                        SendMessage(nextClipboardViewer, m.Msg, m.WParam, m.LParam);
                    break;

                default:
                    base.WndProc(ref m);
                    break;
            }
        }

        void ClipboardDataChanged()
        {
            try
            {
                IDataObject iData = Clipboard.GetDataObject();
                if (iData == null) {
                    Console.WriteLine("Empty content.");
                    return;
                }
                if (iData.GetDataPresent(DataFormats.Text))
                {
                    var content = (string)iData.GetData(DataFormats.Text);
                    if (content == null || content == "") { return; }
                    var history = new History();
                    history.Content = content;
                    history.CreatedAt = DateTime.Now;
                    var db = new DataProcess();
                    db.SaveHistory(history);
                    UpdateTrayMenu();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private void groupList_SelectedIndexChanged(object sender, EventArgs e)
        {
            int selectedIndex = ((ListBox)sender).SelectedIndex;
            if (selectedIndex == -1)
            {
                snippetsList.Items.Clear();
                contentTextBox.Text = "";
                return;
            }
            if (selectedIndex >= groups.Count) { return; }
            ReloadSnippetsList(selectedIndex);
        }

        private void ReloadSnippetsList(int selectedGroupIndex)
        {
            var group = groups[selectedGroupIndex];
            var db = new DataProcess();
            currentSnippets = db.LoadSnippetsInGroup(group);
            snippetsList.Items.Clear(); // Clear snippetsList
            contentTextBox.Text = ""; // Clear contentTextBox
            editSnippetButton.Enabled = deleteSnippetButton.Enabled = false;

            var selectionCount = groupsList.SelectedIndices.Count;
            if (selectionCount == 1)
            {
                deleteGroupButton.Enabled = true;
                renameGroupButton.Enabled = true;
            }
            else if (selectionCount > 1)
            {
                deleteGroupButton.Enabled = true;
                renameGroupButton.Enabled = false;
            }
            else
            {
                deleteGroupButton.Enabled = false;
                renameGroupButton.Enabled = false;
            }
            currentSnippets.ForEach(s => {
                var nameText = resmgr.GetString("__window_content_No_Name", ci);
                if (s.Name.Count() != 0) { nameText = s.Name; }
                snippetsList.Items.Add(nameText);
            });
        }

        private void addGroupButton_Click(object sender, EventArgs e)
        {
            var name = groupNameTextBox.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(resmgr.GetString("__message_box_Group_name_not_empty", ci));
            }
            else
            {
                var db = new DataProcess();
                try
                {
                    db.AddGroup(name);
                    ReloadGroupsUI();
                }
                catch (Exception err)
                {
                    MessageBox.Show(err.ToString());
                }
            }
        }

        private void ReloadGroupsUI()
        {
            DataProcess db = new DataProcess();
            groupNameTextBox.Text = ""; // clear textbox.
            groupsList.Items.Clear();
            groups = db.LoadGroups();
            groups.ForEach((v) => {
                groupsList.Items.Add(v.Name);
            });
        }

        private void renameGroupButton_Click(object sender, EventArgs e)
        {
            var index = groupsList.SelectedIndex;
            var group = groups[index];
            var inputBox = new InputBox(resmgr.GetString("__message_box_message_Remane_group", ci), resmgr.GetString("__message_box_title_Remane", ci), group.Name);
            if (inputBox.ShowDialog() == DialogResult.OK)
            {
                string input = inputBox.InputResult;
                if (input != "")
                {
                    var db = new DataProcess();
                    db.RenameGroup(group, input);
                    ReloadGroupsUI();
                    UpdateTrayMenu();
                }
                groupsList.SelectedIndex = index;
            }

            
        }

        private void historiesList_DoubleClick(object sender, EventArgs e)
        {
            var index = snippetsList.SelectedIndex;
            var content = histories[index].Content;
            if (content == null) { return;  }
            contentTextBox.Text = content;
            if (content.Trim() == "") { return; }
            Clipboard.SetText(content);
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (FormWindowState.Minimized == WindowState)
            {
                Hide();
            }
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (FormWindowState.Normal == WindowState)
            {
                Hide();
                WindowState = FormWindowState.Minimized;
            }
            else {
                MakeKeyAndVisiable();
            }
        }

        private void UpdateTrayMenu()
        {
            reloadSettings();
            var db = new DataProcess();
            histories = db.LoadHistories();
            
            var contextMenu = new ContextMenu();
            organizeHistoriesMenu(contextMenu, histories, SubMenu_Click);
            
            // Add groups
            if (groups == null) { groups = new DataProcess().LoadGroups(); }
            if (groups.Count > 0) { contextMenu.MenuItems.Add("-"); }

            groups.ForEach((g) => {
                MenuItem m = new MenuItem(g.Name);
                contextMenu.MenuItems.Add(m);
                var historiesInGroup = db.LoadSnippetsInGroup(g);
                if (historiesInGroup.Count() == 0)
                {
                    m.Enabled = false;
                }
                else
                {
                    m.Enabled = true;
                }
                if (historiesInGroup.Count() > NUMBER_OF_ITEMS_PER_GROUP) //FIXME: Magic
                {
                    organizeHistoriesMenu(m, historiesInGroup, Snippet_Click);
                }
                else
                {
                    for (int i = 0; i < historiesInGroup.Count(); ++i)
                    {
                        MenuItem subMenu = new MenuItem();
                        var history = historiesInGroup[i];
                        var content = history.Content.Trim();
                        subMenu.Text = shorttenedContent(content.FirstLine());
                        subMenu.Tag = history.Id;
                        subMenu.Click += Snippet_Click;
                        m.MenuItems.Add(subMenu);
                    }
                }
            });

            // Add more menus.
            contextMenu.MenuItems.Add("-");
            contextMenu.MenuItems.Add(EditMenu);
            contextMenu.MenuItems.Add(SettingsMenu);
            contextMenu.MenuItems.Add(DeleteHistoriesMenu);
            contextMenu.MenuItems.Add("-");
            contextMenu.MenuItems.Add(ExitMenu);

            notifyIcon1.ContextMenu = contextMenu;
        }

        private string shorttenedContent(string content)
        {
            var contentLength = content.Count();
            if (contentLength > MAX_MENU_TITLE)
            {
                return content.Substring(0, MAX_MENU_TITLE) + "...";
            }
            else
            {
                return content;
            }
        }

        private void organizeHistoriesMenu(Menu parentMenu, List<History> histories, EventHandler menuEventHandler)
        {
            int totalItems = Math.Min(histories.Count, MAX_COUNT);

            MenuItem menu = new MenuItem(); // dummy 
            for (int i = 0; i < totalItems; i++)
            {
                int remainent = i % NUMBER_OF_ITEMS_PER_GROUP;
                if (remainent == 0)
                {
                    int delta = NUMBER_OF_ITEMS_PER_GROUP;
                    if ((totalItems - i) < NUMBER_OF_ITEMS_PER_GROUP)
                    {
                        delta = (totalItems - i);
                    }
                    menu = new MenuItem();
                    int upperLimit = (i / NUMBER_OF_ITEMS_PER_GROUP) * NUMBER_OF_ITEMS_PER_GROUP + delta;
                    
                    menu.Text = string.Format("{0} - {1}", 1 + i, upperLimit);
                    parentMenu.MenuItems.Add(menu);
                }
                MenuItem subMenu = new MenuItem();
                var history = histories[i];
                var content = history.Content.Trim();
                subMenu.Text = string.Format("{0}. {1}", remainent + 1, shorttenedContent(content.FirstLine()));
                // TODO: Tooltip? 
                subMenu.Tag = history.Id;
                subMenu.Click += menuEventHandler;
                menu.MenuItems.Add(subMenu);
            }
        }

        private void SubMenu_Click(object sender, EventArgs e)
        {
            int tag = (int)((MenuItem)sender).Tag;
            var history = histories.First(h => h.Id == tag);
            var content = history?.Content;
            if (content == null || content.Trim() == "") { return; }
            Clipboard.SetText(content);
        }

        private void Snippet_Click(object sender, EventArgs e)
        {
            int tag = (int)((MenuItem)sender).Tag;
            var db = new DataProcess();
            var history = db.LoadHistoryOfId(tag);
            var content = history?.Content; // In case history is null.
            if (content == null || content.Trim() == "") { return; }
            Clipboard.SetText(content);
        }

        private void MakeKeyAndVisiable()
        {
            Show();
            SetForegroundWindow(Handle.ToInt32());
            WindowState = FormWindowState.Normal;
        }

        private void addSnippetButton_Click(object sender, EventArgs e)
        {
            var selectedGroupIndex = groupsList.SelectedIndex;
            var addForm = new AddSnippetForm();
            addForm.SelectedId = groupsList.SelectedIndex;
            addForm.ShowDialog();
            // Execute after dialog finished?
            ReloadGroupsUI();
            UpdateTrayMenu();
            groupsList.SelectedIndex = selectedGroupIndex;
        }

        private void snippetsList_SelectedIndexChanged(object sender, EventArgs e)
        {
            int selectionCount = snippetsList.SelectedIndices.Count;
            editSnippetButton.Enabled = (selectionCount == 1);
            deleteSnippetButton.Enabled = (selectionCount >= 1);

            contentTextBox.Text = "";
            int selectedIndex = snippetsList.SelectedIndex;
            if (selectionCount == 1)
            {
                if (selectedIndex < 0 || selectedIndex >= currentSnippets.Count()) { return; }
                var snippet = currentSnippets[selectedIndex];
                contentTextBox.Text = snippet.Content;
            }
            else if (selectionCount >= 1)
            {
                contentTextBox.Text = resmgr.GetString("__window_content_multiple_snippests_selected", ci);
            }
        }

        private void deleteGroupButton_Click(object sender, EventArgs e)
        {
            int index = groupsList.SelectedIndex;
            var group = groups[index];

            string message = resmgr.GetString("__message_box_message_delete_group_part_1", ci) + group.Name + resmgr.GetString("__message_box_message_delete_group_part_2", ci); //"All the snippets in \"" + group.Name + "\" group will be deleted.\nAre you sure?";
            string caption = resmgr.GetString("__message_box_title_delete_group_part_1", ci) + group.Name;
            MessageBoxButtons buttons = MessageBoxButtons.YesNo;
            var result = MessageBox.Show(this, message, caption, buttons, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
            if (result == DialogResult.Yes)
            {
                var db = new DataProcess();
                db.DeleteGroup(group);
                // Reload groups in main UI
                ReloadGroupsUI();
                // Clean up UI
                snippetsList.Items.Clear();
                contentTextBox.Text = "";
                // Update tray menu
                UpdateTrayMenu();
            }
        }

        private void editSnippetButton_Click(object sender, EventArgs e)
        {
            int selectionCount = snippetsList.SelectedIndices.Count;
            if (selectionCount != 1) { return; }
            var snippet = currentSnippets[snippetsList.SelectedIndex];
            
            var selectedGroupIndex = groupsList.SelectedIndex;
            var editForm = new AddSnippetForm(snippet);
            editForm.SelectedId = selectedGroupIndex;
            editForm.ShowDialog();
            // Execute after dialog finished?
            ReloadGroupsUI();
            ReloadSnippetsList(selectedGroupIndex);
            UpdateTrayMenu();
            groupsList.SelectedIndex = selectedGroupIndex;
        }

        private void deleteSnippetButton_Click(object sender, EventArgs e)
        {
            string message = resmgr.GetString("__message_box_message_delete_snippets", ci);
            string caption = resmgr.GetString("__message_box_title_delete_snippets", ci);
            MessageBoxButtons buttons = MessageBoxButtons.YesNo;
            var result = MessageBox.Show(this, message, caption, buttons, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
            if (result == DialogResult.Yes)
            {
                var db = new DataProcess();
                var selectedIndices = snippetsList.SelectedIndices;
                for (int i = 0; i < selectedIndices.Count; ++i)
                {
                    int index = selectedIndices[i];
                    db.DeleteHistory(currentSnippets[index]);
                }
                // Clean up UI
                int selectedGroupIndex = groupsList.SelectedIndex;
                ReloadSnippetsList(selectedGroupIndex);
                contentTextBox.Text = "";
                // Update tray menu
                UpdateTrayMenu();
            }
        }

        private void snippetsList_DoubleClick(object sender, EventArgs e)
        {
            var index = snippetsList.SelectedIndex;
            if (index == -1 || index >= groups.Count) { return; }
            var snippet = currentSnippets[index];
            Clipboard.SetText(snippet.Content);
            // FIXME: replace the messagebox with hud
            MessageBox.Show(resmgr.GetString("__message_box_message_copied", ci));
        }

        private void settingsButton_Click(object sender, EventArgs e)
        {
            SettingsMenu_Click(sender, e);
        }
    }
}
