﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace NovaPointWPF.UserControls
{
    /// <summary>
    /// Interaction logic for PermissionForm.xaml
    /// </summary>
    public partial class PermissionForm : UserControl
    {

        public bool UserListOnly
        {
            get { return (bool)GetValue(UserListOnlyProperty); }
            set { SetValue(UserListOnlyProperty, value); }
        }
        public static readonly DependencyProperty UserListOnlyProperty =
            DependencyProperty.Register("UserListOnly", typeof(bool), typeof(PermissionForm), new FrameworkPropertyMetadata(defaultValue: false));

        private bool _detailedReport = true;
        public bool DetailedReport
        {
            get { return _detailedReport; }
            set
            {
                _detailedReport = value;
                if (value) { DetailPanel.Visibility = Visibility.Visible; }
                else
                {
                    DetailPanel.Visibility = Visibility.Collapsed;
                    IncludeAdmins = false;
                    IncludeSiteAccess = false;
                    IncludeUniquePermissions = false;
                }
            }
        }



        public bool IncludeAdmins
        {
            get { return (bool)GetValue(IncludeAdminsProperty); }
            set { SetValue(IncludeAdminsProperty, value); }
        }
        public static readonly DependencyProperty IncludeAdminsProperty =
            DependencyProperty.Register("IncludeAdmins", typeof(bool), typeof(PermissionForm), new FrameworkPropertyMetadata(defaultValue: true));

        public bool IncludeSiteAccess
        {
            get { return (bool)GetValue(IncludeSiteAccessProperty); }
            set { SetValue(IncludeSiteAccessProperty, value); }
        }
        public static readonly DependencyProperty IncludeSiteAccessProperty =
            DependencyProperty.Register("IncludeSiteAccess", typeof(bool), typeof(PermissionForm), new FrameworkPropertyMetadata(defaultValue: true));

        public bool IncludeUniquePermissions
        {
            get { return (bool)GetValue(IncludeUniquePermissionsProperty); }
            set { SetValue(IncludeUniquePermissionsProperty, value); }
        }
        public static readonly DependencyProperty IncludeUniquePermissionsProperty =
            DependencyProperty.Register("IncludeUniquePermissions", typeof(bool), typeof(PermissionForm), new FrameworkPropertyMetadata(defaultValue: true));



        //private bool _singleUser;
        //public bool SingleUser
        //{
        //    get { return _singleUser; }
        //    set
        //    {
        //        _singleUser = value;
        //        if (value) { SingleUserTextbox.Visibility = Visibility.Visible; }
        //        else
        //        {
        //            SingleUserTextbox.Visibility = Visibility.Collapsed;
        //            this.TargetUPN = string.Empty;
        //        }
        //    }
        //}
        //public string TargetUPN
        //{
        //    get { return (string)GetValue(TargetUPNProperty); }
        //    set { SetValue(TargetUPNProperty, value); }
        //}
        //public static readonly DependencyProperty TargetUPNProperty =
        //    DependencyProperty.Register("TargetUPN", typeof(string), typeof(PermissionForm), new PropertyMetadata(string.Empty));

        //public bool Everyone
        //{
        //    get { return TargetEveryone; }
        //    set
        //    {
        //        if (value) { EveryoneLabel.Visibility = Visibility.Visible; }
        //        else { EveryoneLabel.Visibility = Visibility.Collapsed; }
        //        TargetEveryone = value;
        //    }
        //}

        //public bool TargetEveryone
        //{
        //    get { return (bool)GetValue(TargetEveryoneProperty); }
        //    set { SetValue(TargetEveryoneProperty, value); }
        //}
        //public static readonly DependencyProperty TargetEveryoneProperty =
        //    DependencyProperty.Register("TargetEveryone", typeof(bool), typeof(PermissionForm), new FrameworkPropertyMetadata(defaultValue: false));



        public PermissionForm()
        {
            InitializeComponent();
        }

    }
}
