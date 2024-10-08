﻿using NovaPointLibrary.Commands.Authentication;
using NovaPointLibrary.Commands.SharePoint.List;
using NovaPointLibrary.Commands.SharePoint.Site;
using NovaPointLibrary.Solutions;
using NovaPointLibrary.Solutions.Report;
using NovaPointWPF.UserControls;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace NovaPointWPF.Pages.Solutions.Report
{
    /// <summary>
    /// Interaction logic for ListReportForm.xaml
    /// </summary>
    public partial class ListReportForm : Page, ISolutionForm
    {
        public bool IncludeStorageMetrics { get; set; } = true;

        public ListReportForm()
        {
            InitializeComponent();

            DataContext = this;

            SolutionHeader.SolutionTitle = ListReport.s_SolutionName;
            SolutionHeader.SolutionCode = nameof(ListReport);
            SolutionHeader.SolutionDocs = ListReport.s_SolutionDocs;
        }

        public async Task RunSolutionAsync(Action<LogInfo> uiLog, CancellationTokenSource cancelTokenSource)
        {
            ListReportParameters parameters = new(IncludeStorageMetrics, AdminF.Parameters, SiteF.Parameters, ListForm.Parameters);

            await ListReport.RunAsync(parameters, uiLog, cancelTokenSource);
        }
    }
}
