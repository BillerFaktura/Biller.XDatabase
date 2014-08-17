using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XDatabase_Biller
{
    public class EntryPoint : Biller.UI.Interface.IPlugIn
    {
        List<Biller.UI.Interface.IViewModel> internalViewModels;

        public EntryPoint(Biller.UI.ViewModel.MainWindowViewModel parentViewModel)
        {
            this.ParentViewModel = parentViewModel;
            internalViewModels = new List<Biller.UI.Interface.IViewModel>();
        }

        public Biller.UI.ViewModel.MainWindowViewModel ParentViewModel { get; private set; }

        public string Name
        {
            get { return "XDatabase@Biller"; }
        }

        public string Description
        {
            get { return "Fügt eine lokale XML Datenbank zu Biller hinzu"; }
        }

        public double Version
        {
            get { return 0.8; }
        }

        public void Activate()
        {
            ParentViewModel.SettingsTabViewModel.RegisteredDatabases.Add(new Biller.Core.Models.DatabaseUIModel(new Biller.Core.Database.XDatabase(), new SettingsControl()));
        }

        public List<Biller.UI.Interface.IViewModel> ViewModels()
        {
            return internalViewModels;
        }
    }
}
