﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using VSRAD.Package.Options;
using VSRAD.Package.Utils;

namespace VSRAD.Package.ProjectSystem.Profiles
{
    public partial class ProfileOptionsWindow : Window
    {
        // TODO: move in a separate unit-testable class
        private sealed class Context : DefaultNotifyPropertyChanged
        {
            public ProjectOptions Options { get; }

            public List<PropertyPage> Pages => ProfileOptionsReflector.PropertyPages.Value;

            private PropertyPage _selectedPage;
            public PropertyPage SelectedPage { get => _selectedPage; set => SetField(ref _selectedPage, value); }

            public IReadOnlyList<string> ProfileNames => Options.Profiles.Keys.ToList();

            public bool UnsavedChanges { get => SaveCommand.IsEnabled; set => SaveCommand.IsEnabled = value; }

            public WpfDelegateCommand SaveCommand { get; }
            public WpfDelegateCommand RemoveCommand { get; }

            public Context(ProjectOptions projectOptions, Action saveCommand, Action removeCommand)
            {
                Options = projectOptions;
                SaveCommand = new WpfDelegateCommand((_) => saveCommand(), isEnabled: false);
                RemoveCommand = new WpfDelegateCommand((_) => removeCommand(), isEnabled: ProfileNames.Count > 1);
                Options.Profiles.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != nameof(Options.Profiles.Keys)) return;
                    RaisePropertyChanged(nameof(ProfileNames));
                    RemoveCommand.IsEnabled = ProfileNames.Count > 1;
                };
            }
        }

        private readonly PropertyPageEditorWrapper _pageEditor;

        private readonly ProjectOptions _projectOptions;
        private readonly Dictionary<string, Dictionary<PropertyPage, Dictionary<Property, object>>> _propertyValues =
            new Dictionary<string, Dictionary<PropertyPage, Dictionary<Property, object>>>();
        private Dictionary<PropertyPage, Dictionary<Property, object>> SelectedPropertyValues
        {
            get
            {
                if (_propertyValues.TryGetValue(_projectOptions.ActiveProfile, out var values)) return values;
                values = ProfileOptionsReflector.GetPropertyValues(_projectOptions.Profile);
                _propertyValues[_projectOptions.ActiveProfile] = values;
                return values;
            }
        }

        public ProfileOptionsWindow(Macros.MacroEditManager macroEditor, ProjectOptions projectOptions)
        {
            _projectOptions = projectOptions;
            DataContext = new Context(_projectOptions, SaveChanges, RemoveProfile);
            InitializeComponent();
            _pageEditor = new PropertyPageEditorWrapper(PropertiesGrid, macroEditor,
                getValue: (page, property) => SelectedPropertyValues[page][property],
                setValue: (page, property, value) =>
                {
                    ((Context)DataContext).UnsavedChanges = true;
                    SelectedPropertyValues[page][property] = value;
                },
                updateDescription: (description) => DescriptionTextBlock.Text = description,
                getProfileOptions: () => _projectOptions.Profile,
                profileNameChanged: () => ((Context)DataContext).UnsavedChanges = true
                );
            _projectOptions.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_projectOptions.ActiveProfile))
                    _pageEditor.SetupPropertyPageGrid(((Context)DataContext).SelectedPage, _projectOptions.ActiveProfile, true);
            };
            ((Context)DataContext).PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Context.SelectedPage))
                    _pageEditor.SetupPropertyPageGrid(((Context)DataContext).SelectedPage, _projectOptions.ActiveProfile);
            };
            ((Context)DataContext).SelectedPage = ((Context)DataContext).Pages.First();
            OkButton.Click += (s, e) =>
            {
                SaveChanges();
                Close();
            };
            CancelButton.Click += (s, e) => Close();
        }

        private void SaveChanges()
        {
            EditProfileName(_pageEditor.EditedProfileName);
            var profileUpdate = _propertyValues.Select((profileKv) =>
                new KeyValuePair<string, ProfileOptions>(profileKv.Key, ProfileOptionsReflector.ConstructProfileOptions(profileKv.Value)));
            _projectOptions.UpdateProfiles(profileUpdate);
            ((Context)DataContext).UnsavedChanges = false;
        }

        private void Import(object sender, RoutedEventArgs e)
        {
            var fileDialog = new OpenFileDialog { Filter = "Exported profiles (*.json)|*.json" };
            if (fileDialog.ShowDialog() == false) return;

            var manager = new ProfileTransferManager(
                _projectOptions,
                nameConflictResolver: (name) => AskProfileName("Import", ProfileNameWindow.NameConflictMessage(name), name)
            );
            try
            {
                manager.Import(fileDialog.FileName);
            }
            catch
            {
                MessageBox.Show("Unable to read profiles from selected file",
                    "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Export(object sender, RoutedEventArgs e)
        {
            var fileDialog = new SaveFileDialog { Filter = "Exported profiles (*.json)|*.json" };
            if (fileDialog.ShowDialog() == false) return;

            new ProfileTransferManager(_projectOptions, null).Export(fileDialog.FileName);
        }

        private void RemoveProfile()
        {
            _propertyValues.Remove(_projectOptions.ActiveProfile);
            _projectOptions.RemoveProfile(_projectOptions.ActiveProfile);
        }

        private void EditProfileName(string newName)
        {
            var oldName = _projectOptions.ActiveProfile;
            if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

            var profile = _projectOptions.Profile;

            if (_projectOptions.Profiles.Keys.Contains(newName))
                newName = AskProfileName("Rename", ProfileNameWindow.NameConflictMessage(newName), newName);
            if (newName == null) return;

            _propertyValues[newName] = _propertyValues[oldName];

            _propertyValues.Remove(oldName);

            _projectOptions.UpdateProfiles(new[] { new KeyValuePair<string, ProfileOptions>(newName, profile) }); // overwrite the profile under the new name if it exists
            _projectOptions.RemoveProfile(oldName);
            _projectOptions.ActiveProfile = newName;
        }

        private void CreateNewProfile(string dialogTitle, string dialogLabel, ProfileOptions profileOptions)
        {
            var name = AskProfileName(dialogTitle, dialogLabel, "");
            if (string.IsNullOrWhiteSpace(name)) return;

            _projectOptions.AddProfile(name, profileOptions);
            _propertyValues.Remove(name);
            SaveChanges();
        }

        private void CreateNewProfile(object sender, RoutedEventArgs e)
            => CreateNewProfile("Creating a new profile", "Enter the name for the new profile:", new ProfileOptions());

        private void CopyProfile(object sender, RoutedEventArgs e)
            => CreateNewProfile("Copy profile", "Enter the name for the new profile:", _projectOptions.Profile.Clone() as ProfileOptions);

        private string AskProfileName(string dialogTitle, string dialogLabel, string initialName)
        {
            var dialog = new ProfileNameWindow(dialogLabel, okButton: "OK", cancelButton: "Cancel", initialName, () => ((Context)DataContext).ProfileNames)
            {
                Title = dialogTitle
            };
            dialog.ShowDialog();
            return dialog.DialogResult == true ? dialog.EnteredName : null;
        }
    }
}
