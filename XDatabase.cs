using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Biller.Core.Database
{
    public class XDatabase : Interfaces.IDatabase
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public XDatabase(string path)
        {
            if (!path.EndsWith("\\"))
                path = path + "\\";
            DatabasePath = path;
            logger.Info("XDatabase path:" + path);
        }

        public XDatabase()
        { }

        public async Task<bool> Connect()
        {
            var temp = await Task.Run<bool>(() => Initialize());
            IsLoaded = temp;
            return temp;
        }

        private bool Initialize()
        {
            IsFirstLoad = false;
            AdditionalPreviewParsers = new List<Interfaces.DocumentParser>();
            if (String.IsNullOrEmpty(DatabasePath))
                DatabasePath = "Data//";

            if (!Directory.Exists(DatabasePath))
            { 
                try
                {
                    Directory.CreateDirectory(DatabasePath);
                }
                catch (Exception e)
                {
                    logger.Fatal("Could not create data directory", e);
                    return false;
                }
            }
                

            if (!File.Exists(DatabasePath + "Settings.xml"))
            {
                logger.Debug(DatabasePath + "Settings.xml" + " didn't exist -> First load");
                IsFirstLoad = true;
                return false;
            }

            try
            {
                CurrentCompany = new Models.CompanyInformation();
                CurrentCompany.ParseFromXElement(XElement.Parse(File.ReadAllText(DatabasePath + "Settings.xml")).Element("CurrentCompany").Element(CurrentCompany.XElementName));
            }
            catch (Exception e)
            {
                logger.Fatal("Parsing LastCompany failed!", e);
                return false;
            }

            try
            {
                //Orders
                if (!File.Exists(DatabasePath + CurrentCompany.CompanyID + "\\Documents.xml"))
                    using (StreamWriter writer = File.CreateText(DatabasePath + CurrentCompany.CompanyID + "\\Documents.xml"))
                        writer.Write(new XElement(new XElement("Documents")).ToString());
                using (StreamReader reader = File.OpenText(DatabasePath + CurrentCompany.CompanyID + "\\Documents.xml"))
                    DocumentDB = XElement.Load(reader);
                logger.Debug("OrderDB successfully loaded");
            }
            catch (Exception e)
            {
                logger.Fatal("Initializing DocumentDB failed!", e);
                return false;
            }

            try
            {
                // Articles
                if (!File.Exists(DatabasePath + CurrentCompany.CompanyID + "\\Articles.xml"))
                    using (StreamWriter writer = File.CreateText(DatabasePath + CurrentCompany.CompanyID + "\\Articles.xml"))
                        writer.Write(new XElement(new XElement("Articles")).ToString());
                using (StreamReader reader = File.OpenText(DatabasePath + CurrentCompany.CompanyID + "\\Articles.xml"))
                    ArticleDB = XElement.Load(reader);
                logger.Debug("ArticleDB successfully loaded");
            }
            catch (Exception e)
            {
                logger.Fatal("Initializing ArticleDB failed!", e);
                return false;
            }

            try
            {
                // Customers
                if (!File.Exists(DatabasePath + CurrentCompany.CompanyID + "\\Customers.xml"))
                    using (StreamWriter writer = File.CreateText(DatabasePath + CurrentCompany.CompanyID + "\\Customers.xml"))
                        writer.Write(new XElement(new XElement("Customers")).ToString());
                using (StreamReader reader = File.OpenText(DatabasePath + CurrentCompany.CompanyID + "\\Customers.xml"))
                    CustomerDB = XElement.Load(reader);
                logger.Debug("CustomerDB successfully loaded");
            }
            catch (Exception e)
            {
                logger.Fatal("Initializing CustomerDB failed!", e);
                return false;
            }

            try
            {
                // Others
                if (!File.Exists(DatabasePath + CurrentCompany.CompanyID + "\\Others.xml"))
                    using (StreamWriter writer = File.CreateText(DatabasePath + CurrentCompany.CompanyID + "\\Others.xml"))
                        writer.Write(new XElement(new XElement("Settings")).ToString());
                using (StreamReader reader = File.OpenText(DatabasePath + CurrentCompany.CompanyID + "\\Others.xml"))
                    SettingsDB = XElement.Load(reader);
                logger.Debug("SettingsDB successfully loaded");
            }
            catch (Exception e)
            {
                logger.Fatal("Initializing SettingsDB failed!", e);
                return false;
            }

            OtherDBs = new List<XElement>();
            if (RegisteredAdditionalDBs == null)
                RegisteredAdditionalDBs = new List<Interfaces.IXMLStorageable>();
            else
            {
                foreach (var item in RegisteredAdditionalDBs)
                    registerStorageableItem(item, false);
            }
            logger.Info("XDatabase connected");
            return true;
        }

        public bool IsFirstLoad { get; private set; }
        public bool IsLoaded { get; private set; }
        private string DatabasePath { get; set; }
        private XElement DocumentDB { get; set; }
        private XElement ArticleDB { get; set; }
        private XElement CustomerDB { get; set; }
        private XElement SettingsDB { get; set; }
        private List<XElement> OtherDBs { get; set; }
        private List<Interfaces.IXMLStorageable> RegisteredAdditionalDBs { get; set; }

        /// <summary>
        /// Returns an enumerable collection of all registered companies.
        /// <remarks>This function is awaitable to ensure UI experience.</remarks>
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<Models.CompanyInformation>> GetCompanyList()
        {
            return await Task.Run<IEnumerable<Models.CompanyInformation>>(() => getCompanyList());
        }

        private IEnumerable<Models.CompanyInformation> getCompanyList()
        {
            logger.Trace("Start getting company list");
            XElement CompanySettings;
            var temp = new ObservableCollection<Models.CompanyInformation>();
            foreach (string dir in System.IO.Directory.GetDirectories(DatabasePath))
            {
                logger.Trace("Parsing " + dir + "\\Others.xml");
                try 
                {
                    using (StreamReader reader = File.OpenText(dir + "\\Others.xml"))
                        CompanySettings = XElement.Load(reader);
                }
                catch (Exception e)
                {
                    logger.Trace("Error parsing " + dir + "\\Others.xml", e);
                    continue;
                }
                var item = new Models.CompanyInformation();
                item.ParseFromXElement(CompanySettings.Element(item.XElementName));
                try { temp.Add(item); }
                catch (Exception e) { logger.Trace("Error creating CompanyInformation", e); }
            }
            logger.Trace("Finished creating company list with " + temp.Count.ToString() + " items");
            return temp;
        }

        public Models.CompanyInformation CurrentCompany { get; private set; }

        public async Task<bool> ChangeCompany(Models.CompanyInformation target)
        {
            logger.Info("Request to change company to " + target.CompanyName + " (" + target.CompanyID + ")");
            XElement doc;

            if (!File.Exists(DatabasePath + "Settings.xml"))
            {
                logger.Debug(DatabasePath + "Settings.xml" + " didn't exist. Maybe no company was set before.");
                doc = new XElement("ApplicationSettings", new XElement("CurrentCompany", target.GetXElement()));
            }
            else
            {
                //Case when FirstLoad is true
                try
                {
                    doc = XElement.Parse(File.ReadAllText(DatabasePath + "Settings.xml", Encoding.UTF8));
                    doc.Element("CurrentCompany").Element(CurrentCompany.XElementName).ReplaceWith(target.GetXElement());
                }
                catch (Exception e) { logger.Fatal("Error while parsing or replacing the current company. Exiting the methode without changeing the company.", e); return false; }
            }
            try { File.WriteAllText(DatabasePath + "Settings.xml", doc.ToString(), Encoding.UTF8); }
            catch (Exception e) { logger.Fatal("Could not write the changes into the file", e); }
            logger.Info("Reconnecting database");
            return await Connect();
        }

        public void AddCompany(Models.CompanyInformation source)
        {
            logger.Info("Adding new company " + source.CompanyName + "(" + source.CompanyID + ")");
            try { System.IO.Directory.CreateDirectory(DatabasePath + source.CompanyID); }
            catch (Exception e) { logger.Fatal("Error creating directory", e); }

            try
            {
                var temp = new XElement("Settings", source.GetXElement());
                File.WriteAllText(DatabasePath + source.CompanyID + "\\Others.xml", temp.ToString(), Encoding.UTF8);
            }
            catch (Exception e) { logger.Fatal("Error saving the initial settings document", e); }
        }

        
        // Common data //

        public async Task<IEnumerable<Utils.PaymentMethode>> PaymentMethodes()
        {
            return await Task<IEnumerable<Utils.PaymentMethode>>.Run(() => paymentMethodesList());
        }

        private IEnumerable<Utils.PaymentMethode> paymentMethodesList()
        {
            logger.Debug("Getting PaymentMethodes-list");
            var templist = new ObservableCollection<Utils.PaymentMethode>();
            if (!SettingsDB.Elements("PaymentMethodes").Any())
                return templist;
            var itemlist = SettingsDB.Element("PaymentMethodes").Elements("PaymentMethode");
            foreach (XElement item in itemlist)
            {
                try
                {
                    var tempitem = new Utils.PaymentMethode();
                    tempitem.ParseFromXElement(item);
                    templist.Add(tempitem);
                }
                catch (Exception e) { logger.Fatal("Error parsing the element", e); }
            }
            return templist;
        }

        public void SaveOrUpdatePaymentMethode(Utils.PaymentMethode source)
        {
            if (!SettingsDB.Elements("PaymentMethodes").Any())
                SettingsDB.Add(new XElement("PaymentMethodes"));

            if (SettingsDB.Element("PaymentMethodes").Elements(source.XElementName).Any(x => x.Element("Name").Value == source.Name))
                SettingsDB.Element("PaymentMethodes").Elements(source.XElementName).Single(x => x.Element("Name").Value == source.Name).ReplaceWith(source.GetXElement());
            else
                SettingsDB.Element("PaymentMethodes").Add(source.GetXElement());

            try { File.WriteAllText(DatabasePath + CurrentCompany.CompanyID + "\\Others.xml", SettingsDB.ToString()); }
            catch (Exception e) { logger.Fatal("Error saving the file " + DatabasePath + CurrentCompany.CompanyID + "\\Others.xml. PaymentMethodes was changed.", e); }
        }

        public async Task<IEnumerable<Utils.TaxClass>> TaxClasses()
        {
            return await Task<Utils.TaxClass>.Run(() => TaxClassesList());
        }

        private IEnumerable<Utils.TaxClass> TaxClassesList()
        {
            logger.Debug("Start getting TaxClass-list");
            var templist = new ObservableCollection<Utils.TaxClass>();
            if (!SettingsDB.Elements("TaxClasses").Any())
                return templist;
            var itemlist = SettingsDB.Element("TaxClasses").Elements("TaxClass");
            foreach (XElement item in itemlist)
            {
                try
                {
                    var tempitem = new Utils.TaxClass();
                    tempitem.ParseFromXElement(item);
                    templist.Add(tempitem);
                }
                catch (Exception e) { logger.Fatal("Error parsing the element", e); }
            }
            return templist;
        }

        public void SaveOrUpdateTaxClass(Utils.TaxClass source)
        {
            if (!SettingsDB.Elements("TaxClasses").Any())
                SettingsDB.Add(new XElement("TaxClasses"));

            if (SettingsDB.Element("TaxClasses").Elements(source.XElementName).Any(x => x.Element("Name").Value == source.Name))
                SettingsDB.Element("TaxClasses").Elements(source.XElementName).Single(x => x.Element("Name").Value == source.Name).ReplaceWith(source.GetXElement());
            else
                SettingsDB.Element("TaxClasses").Add(source.GetXElement());

            
            try { File.WriteAllText(DatabasePath + CurrentCompany.CompanyID + "\\Others.xml", SettingsDB.ToString()); }
            catch (Exception e) { logger.Fatal("Error saving changes to " + DatabasePath + CurrentCompany.CompanyID + "\\Others.xml" + "TaxClass was changed.", e); }
        }

        public async Task<IEnumerable<Utils.Unit>> ArticleUnits()
        {
            return await Task<Utils.TaxClass>.Run(() => ArticleUnitsList());
        }

        private IEnumerable<Utils.Unit> ArticleUnitsList()
        {
            logger.Debug("Start getting Units-list");
            var templist = new ObservableCollection<Utils.Unit>();
            if (!SettingsDB.Elements("Units").Any())
                return templist;
            var itemlist = SettingsDB.Element("Units").Elements("Unit");
            foreach (XElement item in itemlist)
            {
                try
                {
                    var tempitem = new Utils.Unit();
                    tempitem.ParseFromXElement(item);
                    templist.Add(tempitem);
                }
                catch (Exception e) { logger.Fatal("Error parsing the element", e); }
            }
            return templist;
        }

        public void SaveOrUpdateArticleUnit(Utils.Unit source)
        {
            if (!SettingsDB.Elements("Units").Any())
                SettingsDB.Add(new XElement("Units"));

            if (SettingsDB.Element("Units").Elements(source.XElementName).Any(x => x.Element("Name").Value == source.Name))
                SettingsDB.Element("Units").Elements(source.XElementName).Single(x => x.Element("Name").Value == source.Name).ReplaceWith(source.GetXElement());
            else
                SettingsDB.Element("Units").Add(source.GetXElement());
            
            try { File.WriteAllText(DatabasePath + CurrentCompany.CompanyID + "\\Others.xml", SettingsDB.ToString()); }
            catch (Exception e) { logger.Fatal("Error writing article-unit to " + DatabasePath + CurrentCompany.CompanyID + "\\Others.xml", e); }
        }

        public void SaveOrUpdateSettings(Utils.KeyValueStore settings)
        {
            if (!SettingsDB.Elements("Settings").Any())
                SettingsDB.Add(new XElement("Settings"));
            SettingsDB.Element("Settings").Value = Newtonsoft.Json.JsonConvert.SerializeObject(settings);

            try { File.WriteAllText(DatabasePath + CurrentCompany.CompanyID + "\\Others.xml", SettingsDB.ToString()); }
            catch (Exception e) { logger.Fatal("Error saving changes to " + DatabasePath + CurrentCompany.CompanyID + "\\Others.xml" + "TaxClass was changed.", e); }
        }

        public async Task<Utils.KeyValueStore> GetSettings()
        {
            return await Task<Utils.KeyValueStore>.Run(() => getSettings());
        }

        private Utils.KeyValueStore getSettings()
        {
            if (!SettingsDB.Elements("Settings").Any())
                return new Utils.KeyValueStore();
            XElement item = SettingsDB.Element("Settings");
            dynamic response = Newtonsoft.Json.JsonConvert.DeserializeObject<Utils.KeyValueStore>(item.Value);
            return response;
        }

        #region Articles

        private List<string> registeredArticleIDs = new List<string>();

        public async Task<IEnumerable<Articles.PreviewArticle>> AllArticles()
        {
            return await Task <IEnumerable<Articles.PreviewArticle>>.Run(() => GetAllArticles());
        }

        private IEnumerable<Articles.PreviewArticle> GetAllArticles()
        {
            var sw = new Performance.Stopwatch("GetAllArticles");
            sw.Start();

            logger.Debug("Start getting article list");
            var templist = new ObservableCollection<Articles.PreviewArticle>();
            if (!ArticleDB.Elements("Article").Any())
                return templist;

            var itemlist = ArticleDB.Elements("Article");
            foreach (XElement item in itemlist)
            {
                try
                {
                    dynamic tempitem = new Articles.PreviewArticle();
                    tempitem.ArticleID = item.Element("ArticleID").Value;
                    tempitem.ArticleDescription = item.Element("ArticleDescription").Value;
                    tempitem.ArticlePrice.ParseFromXElement(item.Element("Price1").Element("PriceGroup").Element("Money"));
                    tempitem.ArticleUnit = item.Element("ArticleUnit").Value;
                    templist.Add(tempitem);
                }
                catch (Exception e) { logger.Fatal("Error parsing the in 'GetAllArticles': "+ item.ToString(), e); }
            }
            sw.Stop();
            logger.Info(sw.Result(templist.Count));

            return templist;
        }

        public async Task<bool> SaveOrUpdateArticle(Articles.Article source)
        {
            return await Task.Run<bool>(() => saveOrUpdateArticle(source));
        }

        public async Task<bool> SaveOrUpdateArticle(IEnumerable<Articles.Article> source)
        {
            return await Task.Run<bool>(() => saveOrUpdateArticle(source));
        }

        private bool saveOrUpdateArticle(Articles.Article source)
        {
            var sw = new Performance.Stopwatch("saveOrUpdateArticle");
            sw.Start();
            if (!ArticleDB.AncestorsAndSelf("Articles").Any())
                ArticleDB.Add(new XElement("Articles"));

            if (ArticleDB.Elements("Article").Any(x => x.Element("ArticleID").Value == source.ArticleID))
                ArticleDB.Elements("Article").Single(x => x.Element("ArticleID").Value == source.ArticleID).ReplaceWith(source.GetXElement());
            else
                ArticleDB.Add(source.GetXElement());

            try { File.WriteAllText(DatabasePath + CurrentCompany.CompanyID + "\\Articles.xml", ArticleDB.ToString()); }
            catch (Exception e) { logger.Fatal("Error writing article to " + DatabasePath + CurrentCompany.CompanyID + "\\Articles.xml", e); return false; }
            sw.Stop();
            logger.Info(sw.Result());
            return true;
        }

        private bool saveOrUpdateArticle(IEnumerable<Articles.Article> source)
        {
            var sw = new Performance.Stopwatch("saveOrUpdateArticle");
            sw.Start();
            if (!ArticleDB.AncestorsAndSelf("Articles").Any())
                ArticleDB.Add(new XElement("Articles"));

            foreach (var item in source)
            {
                if (ArticleDB.Elements("Article").Any(x => x.Element("ArticleID").Value == item.ArticleID))
                    ArticleDB.Elements("Article").Single(x => x.Element("ArticleID").Value == item.ArticleID).ReplaceWith(item.GetXElement());
                else
                    ArticleDB.Add(item.GetXElement());
            }
            
            try { File.WriteAllText(DatabasePath + CurrentCompany.CompanyID + "\\Articles.xml", ArticleDB.ToString()); }
            catch (Exception e) { logger.Fatal("Error writing article to " + DatabasePath + CurrentCompany.CompanyID + "\\Articles.xml", e); return false; }
            sw.Stop();
            logger.Info(sw.Result(source.Count()));
            return true;
        }

        public async Task<bool> ArticleExists(string ArticleID)
        {
            return await Task<bool>.Run(() => CheckIfArticleExists(ArticleID));
        }

        private bool CheckIfArticleExists(string ArticleID)
        {
            if (registeredArticleIDs.Contains(ArticleID))
                return true;

            if (!ArticleDB.Elements("Article").Any())
                return false;

            if (ArticleDB.Elements("Article").Any(x => x.Element("ArticleID").Value == ArticleID))
                return true;
            else
                return false;
        }
        
        /// <summary>
        /// Returns an article with the given ArticleID.\n
        /// This function is awaitable.
        /// </summary>
        /// <param name="ArticleID">The unique identifier of the article you are looking for</param>
        /// <returns></returns>
        public async Task<Articles.Article> GetArticle(string ArticleID)
        {
            return await Task<Articles.Article>.Run(() => getArticle(ArticleID));
        }

        private Articles.Article getArticle(string ArticleID)
        {
            if (!ArticleDB.Elements("Article").Any())
                return new Articles.Article();

            if (ArticleDB.Elements("Article").Any(x => x.Element("ArticleID").Value == ArticleID))
            {
                var sw = new Performance.Stopwatch("GetArticle (" + ArticleID + ")");
                sw.Start();
                var temp = new Core.Articles.Article();
                var xitem = ArticleDB.Elements("Article").Single(x => x.Element("ArticleID").Value == ArticleID);
                temp.ParseFromXElement(xitem);
                temp.TaxClass = TaxClassesList().SingleOrDefault(x => x.Name == xitem.Element("TaxClass").Value);
                temp.ArticleUnit = ArticleUnitsList().SingleOrDefault(x => x.Name == xitem.Element("ArticleUnit").Value);

                sw.Stop();
                sw.PrintResultToConsole();
                logger.Trace(sw.Result());

                return temp;
            }
            else
                return new Articles.Article();
        }

        public async Task<int> GetNextArticleID()
        {
            return await Task<int>.Run(() => getNextArticleID());
        }

        private int getNextArticleID()
        {
            if (!ArticleDB.Elements("Article").Any())
                return 1000;

            var list = from article in ArticleDB.Elements("Article") orderby article.Element("ArticleID").Value select article.Element("ArticleID").Value;
            int output;
            var lastitem = list.Last().ToString();

            var temps = from customer in registeredArticleIDs orderby customer select customer;

            string lasttempitem = "";
            if (temps.Count() > 0)
                lasttempitem = temps.Last().ToString();

            if (int.TryParse(lastitem, out output))
            {
                int output2;
                if (int.TryParse(lasttempitem, out output2))
                    if (output2 > output)
                        return output2 + 1;
                return output + 1;
            }
            return -1;
        }

        public async Task<bool> UpdateTemporaryUsedArticleID(string oldvalue, string newvalue)
        {
            return await Task.Run<bool>(() => updateTemoraryUsedArticleID(oldvalue, newvalue));
        }

        private bool updateTemoraryUsedArticleID(string oldvalue, string newvalue)
        {
            registeredArticleIDs.Remove(oldvalue);
            if (!string.IsNullOrEmpty(newvalue))
                registeredArticleIDs.Add(newvalue);
            return true;
        }

        #endregion

        #region Customers
        private List<string> registeredCustomerIDs = new List<string>();

        public async Task<IEnumerable<Customers.PreviewCustomer>> AllCustomers()
        {
            return await Task<IEnumerable<Customers.PreviewCustomer>>.Run(() => GetAllCustomers());
        }

        public async Task<Customers.Customer> GetCustomer(string CustomerID)
        {
            return await Task<Customers.Customer>.Run(() => getCustomer(CustomerID));
        }

        public async Task<int> GetNextCustomerID()
        {
            return await Task<int>.Run(() => getNextCustomerID());
        }

        public async Task<bool> SaveOrUpdateCustomer(Customers.Customer source)
        {
            return await Task.Run<bool>(() => saveOrUpdateCustomer(source));
        }

        public async Task<bool> SaveOrUpdateCustomer(IEnumerable<Customers.Customer> source)
        {
            return await Task.Run<bool>(() => saveOrUpdateCustomer(source));
        }

        private bool saveOrUpdateCustomer(Customers.Customer source)
        {
            var sw = new Performance.Stopwatch("SaveOrUpdateCustomer");
            sw.Start();
            if (!CustomerDB.AncestorsAndSelf("Customers").Any())
                CustomerDB.Add(new XElement("Customers"));

            if (CustomerDB.Elements("Customer").Any(x => x.Element("CustomerID").Value == source.CustomerID))
                CustomerDB.Elements("Customer").Single(x => x.Element("CustomerID").Value == source.CustomerID).ReplaceWith(source.GetXElement());
            else
                CustomerDB.Add(source.GetXElement());
            
            try { File.WriteAllText(DatabasePath + CurrentCompany.CompanyID + "\\Customers.xml", CustomerDB.ToString()); }
            catch (Exception e) { logger.Fatal("Error writing article to " + DatabasePath + CurrentCompany.CompanyID + "\\Customers.xml", e); return false; }
            sw.Stop();
            logger.Info(sw.Result());
            return true;
        }

        private bool saveOrUpdateCustomer(IEnumerable<Customers.Customer> source)
        {
            var sw = new Performance.Stopwatch("SaveOrUpdateCustomer");
            sw.Start();
            if (!CustomerDB.AncestorsAndSelf("Customers").Any())
                CustomerDB.Add(new XElement("Customers"));

            foreach (var item in source)
            {
                if (CustomerDB.Elements("Customer").Any(x => x.Element("CustomerID").Value == item.CustomerID))
                    CustomerDB.Elements("Customer").Single(x => x.Element("CustomerID").Value == item.CustomerID).ReplaceWith(item.GetXElement());
                else
                    CustomerDB.Add(item.GetXElement());
            }
            
            try { File.WriteAllText(DatabasePath + CurrentCompany.CompanyID + "\\Customers.xml", CustomerDB.ToString()); }
            catch (Exception e) { logger.Fatal("Error writing article to " + DatabasePath + CurrentCompany.CompanyID + "\\Customers.xml", e); return false; }
            sw.Stop();
            logger.Info(sw.Result(source.Count()));
            return true;
        }

        public async Task<bool> CustomerExists(string CustomerID)
        {
            return await Task<bool>.Run(() => CheckIfCustomerExists(CustomerID));
        }

        private IEnumerable<Customers.PreviewCustomer> GetAllCustomers()
        {
            var sw = new Performance.Stopwatch("GetAllCustomers");
            sw.Start();

            logger.Debug("Start getting customer list");
            var templist = new ObservableCollection<Customers.PreviewCustomer>();
            if (!CustomerDB.Elements("Customer").Any())
                return templist;

            var itemlist = CustomerDB.Elements("Customer");
            foreach (XElement item in itemlist)
            {
                try
                {
                    dynamic tempitem = new Customers.PreviewCustomer();
                    tempitem.CustomerID = item.Element("CustomerID").Value;
                    tempitem.DisplayName = item.Element("DisplayName").Value;
                    var address = new Utils.Address();
                    address.ParseFromXElement(item.Element("MainAddress").Element(address.XElementName));
                    tempitem.Address = address.OneLineString;
                    templist.Add(tempitem);
                }
                catch (Exception e) { logger.Fatal("Error parsing the in 'GetAllCustomers': " + item.ToString(), e); }
            }
            sw.Stop();
            logger.Info(sw.Result(templist.Count));

            return templist;
        }

        private Customers.Customer getCustomer(string CustomerID)
        {
            if (!CustomerDB.Elements("Customer").Any())
                return new Customers.Customer();

            if (CustomerDB.Elements("Customer").Any(x => x.Element("CustomerID").Value == CustomerID))
            {
                var sw = new Performance.Stopwatch("GetCustomer (" + CustomerID + ")");
                sw.Start();
                var temp = new Customers.Customer();
                var xitem = CustomerDB.Elements("Customer").Single(x => x.Element("CustomerID").Value == CustomerID);
                temp.ParseFromXElement(xitem);
                if (paymentMethodesList().Any(x => x.Name == xitem.Element("DefaultPaymentMethode").Value))
                    temp.DefaultPaymentMethode = paymentMethodesList().First(x => x.Name == xitem.Element("DefaultPaymentMethode").Value);
                sw.Stop();
                sw.PrintResultToConsole();
                logger.Trace(sw.Result());

                return temp;
            }
            else
                return new Customers.Customer();
        }

        private bool CheckIfCustomerExists(string CustomerID)
        {
            if (registeredCustomerIDs.Contains(CustomerID))
                return true;

            if (!CustomerDB.Elements("Customer").Any())
                return false;

            if (CustomerDB.Elements("Customer").Any(x => x.Element("CustomerID").Value == CustomerID))
                return true;
            else
                return false;
        }

        private int getNextCustomerID()
        {
            if (!CustomerDB.Elements("Customer").Any())
                return 1000;

            var list = from customer in CustomerDB.Elements("Customer") orderby customer.Element("CustomerID").Value select customer.Element("CustomerID").Value;
            var temps = from customer in registeredCustomerIDs orderby customer select customer;

            string lasttempitem = "" ;
            if (temps.Count() > 0)
                lasttempitem = temps.Last().ToString();
            
            int output;
            var lastitem = list.Last().ToString();
            if (int.TryParse(lastitem, out output))
            {
                int output2;
                if (int.TryParse(lasttempitem, out output2))
                    if (output2 > output)
                        return output2 + 1;
                return output + 1;
            }
            return -1;
        }

        public async Task<bool> UpdateTemporaryUsedCustomerID(string oldvalue, string newvalue)
        {
            return await Task.Run<bool>(() => updateTemporaryUsedCustomerID(oldvalue, newvalue));
        }

        private bool updateTemporaryUsedCustomerID(string oldvalue, string newvalue)
        {
            try
            {
                registeredCustomerIDs.Remove(oldvalue);
                if (!string.IsNullOrEmpty(newvalue))
                    registeredCustomerIDs.Add(newvalue);
                return true;
            }
            catch (Exception e)
            {
                logger.Fatal("Exception in 'private bool updateTemporaryUsedCustomerID'", e);
                return false;
            }
            
        }
        #endregion

        #region Documents
        private List<KeyValuePair<string, string>> registeredDocumentIDs = new List<KeyValuePair<string, string>>();

        public async Task<IEnumerable<Document.PreviewDocument>> DocumentsInInterval(DateTime IntervalStart, DateTime IntervalEnd)
        {
            return await Task<IEnumerable<Document.PreviewDocument>>.Run(() => documentsInInterval(IntervalStart, IntervalEnd, string.Empty));
        }

        public async Task<IEnumerable<Document.PreviewDocument>> DocumentsInInterval(DateTime IntervalStart, DateTime IntervalEnd, string DocumentType)
        {
            return await Task<IEnumerable<Document.PreviewDocument>>.Run(() => documentsInInterval(IntervalStart, IntervalEnd, DocumentType));
        }

        private ObservableCollection<Document.PreviewDocument> documentsInInterval(DateTime IntervalStart, DateTime IntervalEnd, string DocumentType)
        {
            var sw = new Performance.Stopwatch("documentsInInterval");
            sw.Start();

            var output = new ObservableCollection<Document.PreviewDocument>();

            if (!DocumentDB.AncestorsAndSelf("Documents").Any())
                return output;

            if (!String.IsNullOrEmpty(DocumentType))
            {
                var list = from document in DocumentDB.Elements() where document.Name == DocumentType && IntervalStart <= DateTime.Parse(document.Element("Date").Value) && IntervalEnd >= DateTime.Parse(document.Element("Date").Value) select document;
                foreach (XElement item in list)
                {
                    dynamic temp = new Document.PreviewDocument(item.Name.ToString());
                    temp.DocumentID = item.Element("ID").Value;
                    temp.Customer = item.Element("CustomerPreview").Value;
                    temp.Date = DateTime.Parse(item.Element("Date").Value);

                    foreach (Interfaces.DocumentParser parser in AdditionalPreviewParsers.Where(x => x.DocumentType == item.Name.ToString()))
                    { var result = parser.ParseAdditionalPreviewData(ref temp, item); }

                    output.Add(temp);
                }
            }
            else 
            {
                var list = from document in DocumentDB.Elements() where IntervalStart <= DateTime.Parse(document.Element("Date").Value) && IntervalEnd >= DateTime.Parse(document.Element("Date").Value) select document;
                foreach (XElement item in list)
                {
                    dynamic temp = new Document.PreviewDocument(item.Name.ToString());
                    temp.DocumentID = item.Element("ID").Value;
                    temp.Customer = item.Element("PreviewCustomer").Value;
                    temp.Date = DateTime.Parse(item.Element("Date").Value);

                    foreach (Interfaces.DocumentParser parser in AdditionalPreviewParsers.Where(x => x.DocumentType == item.Name.ToString()))
                    { var result = parser.ParseAdditionalPreviewData(ref temp, item); }

                    output.Add(temp);
                }
            }

            logger.Info(sw.Result(output.Count));
            return output;
        }

        public async Task<bool> DocumentExists(Document.Document source)
        {
            return await Task<bool>.Run(() => documentExists(source));
        }

        private bool documentExists(Document.Document source)
        {
            if (registeredDocumentIDs.Contains(new KeyValuePair<string, string>(source.DocumentType, source.DocumentID)))
                return true;

            if (!DocumentDB.AncestorsAndSelf("Documents").Any())
                return false;

            if (DocumentDB.Elements().Any(x => x.Element(source.DocumentType).Element("ID").Value == source.DocumentID))
                return true;

            return false;
        }

        public async Task<int> GetNextDocumentID(string DocumentType)
        {
            return await Task<int>.Run(() => getNextDocumentID(DocumentType));
        }

        private int getNextDocumentID(string DocumentType)
        {
            var list = from document in DocumentDB.Elements(DocumentType) orderby document.Element("ID").Value select document.Element("ID").Value;
            var temps = from document in registeredDocumentIDs where document.Key == DocumentType orderby document.Value select document.Value;

            string lasttempitem = "";
            if (temps.Count() > 0)
                lasttempitem = temps.Last().ToString();

            int output;
            if (list.Count() == 0 && string.IsNullOrEmpty(lasttempitem))
                return 1000;
            if (list.Count() == 0)
            {
                int output2;
                if (int.TryParse(lasttempitem, out output2))
                    return output2 + 1;
            }

            var lastitem = list.Last().ToString();
            if (int.TryParse(lastitem, out output))
            {
                int output2;
                if (int.TryParse(lasttempitem, out output2))
                    if (output2 > output)
                        return output2 + 1;
                return output + 1;
            }
            return -1;
        }

        public async Task<Document.Document> GetDocument(Document.Document source)
        {
            return await Task<Document.Document>.Run(() => getDocument(source));
        }

        private Document.Document getDocument(Document.Document source)
        {
            if (!DocumentDB.AncestorsAndSelf("Documents").Any())
                return source;

            if (DocumentDB.Elements(source.DocumentType).Any(x => x.Element("ID").Value == source.DocumentID))
            {
                logger.Debug("Reading " + source.DocumentType + " with ID " + source.DocumentID);
                var xelement = DocumentDB.Elements(source.DocumentType).Single(x => x.Element("ID").Value == source.DocumentID);
                source.ParseFromXElement(xelement);

                foreach (Interfaces.DocumentParser parser in AdditionalPreviewParsers.Where(x => x.DocumentType == xelement.Name))
                {
                    try
                    {
                        parser.ParseAdditionalData(ref source, xelement, this); 
                    }
                    catch (Exception e)
                    {
                        logger.Error("Error at ParseAdditionalData", e);
                    }
                }
            }

            return source;
        }

        public async Task<bool> SaveOrUpdateDocument(Document.Document source)
        {
            return await Task<bool>.Run(() => saveOrUpdateDocument(source));
        }

        private bool saveOrUpdateDocument(Document.Document source)
        {
            //Performance measurement
            var sw = new Performance.Stopwatch("saveOrUpdateDocument");
            sw.Start();
            logger.Debug("Receiving SaveOrUpdate for " + source.DocumentType + " with ID " + source.DocumentID);

            if (!DocumentDB.AncestorsAndSelf("Documents").Any())
                DocumentDB.Add(new XElement("Documents"));

            if (DocumentDB.Elements(source.DocumentType).Any(x => x.Element("ID").Value == source.DocumentID))
            {
                logger.Debug("Updating " + source.DocumentType + " with ID " + source.DocumentID);
                DocumentDB.Elements(source.DocumentType).Single(x => x.Element("ID").Value == source.DocumentID).ReplaceWith(source.GetXElement());
            }
            else
            {
                logger.Debug("Adding " + source.DocumentType + " with ID " + source.DocumentID);
                DocumentDB.Add(source.GetXElement());
            }
            try { File.WriteAllText(DatabasePath + CurrentCompany.CompanyID + "\\Documents.xml", DocumentDB.ToString()); }
            catch (Exception e) { logger.Fatal("Error writing Document to " + DatabasePath + CurrentCompany.CompanyID + "\\Documents.xml", e); return false; }
            sw.Stop();
            logger.Info(sw.Result());

            return true;
        }

        public async Task<bool> UpdateTemporaryUsedDocumentID(string oldID, string newID, string DocumentType)
        {
            return await Task<bool>.Run(() => updateTemporaryUsedDocumentID(oldID, newID, DocumentType));
        }

        private bool updateTemporaryUsedDocumentID(string oldID, string newID, string DocumentType)
        {
            try
            {
                registeredDocumentIDs.Remove(new KeyValuePair<string, string>(DocumentType, oldID));
                if (!string.IsNullOrEmpty(newID))
                    registeredDocumentIDs.Add(new KeyValuePair<string, string>(DocumentType, newID));
                return true;
            }
            catch (Exception e)
            {
                logger.Fatal("Exception in 'private bool updateTemporaryUsedDocumentID'", e);
                return false;
            }
        }

        private List<Interfaces.DocumentParser> AdditionalPreviewParsers;
        public async Task<bool> AddAdditionalPreviewDocumentParser(Interfaces.DocumentParser parser)
        {
            return await Task<bool>.Run(() => addAdditionalPreviewDocumentParser(parser));
        }

        private bool addAdditionalPreviewDocumentParser(Interfaces.DocumentParser parser)
        {
            logger.Info("Added PreviewDocumentParser for " + parser.DocumentType);
            AdditionalPreviewParsers.Add(parser);
            return true;
        }
        #endregion

        public async Task<IEnumerable<Interfaces.IXMLStorageable>> AllStorageableItems(Interfaces.IXMLStorageable referenceStorageable)
        {
            return await Task<IEnumerable<Interfaces.IXMLStorageable>>.Run(() => allStorageableItems(referenceStorageable));
        }

        private IEnumerable<Interfaces.IXMLStorageable> allStorageableItems(Interfaces.IXMLStorageable referenceStorageable)
        {
            var sw = new Performance.Stopwatch("allStorageableItems: " + referenceStorageable.XElementName);
            sw.Start();

            var list = new ObservableCollection<Interfaces.IXMLStorageable>();

            var isregistered = from registered in RegisteredAdditionalDBs where registered.XElementName == referenceStorageable.XElementName select registered;
            if (isregistered.Count() == 0)
                return list;

            XElement db = (from dbs in OtherDBs where dbs.Name == (referenceStorageable.XElementName + "s") select dbs).First();

            if (!db.AncestorsAndSelf(referenceStorageable.XElementName + "s").Any())
                return list;

            var itemlist = db.AncestorsAndSelf(referenceStorageable.XElementName + "s").Elements(referenceStorageable.XElementName);
            foreach (XElement item in itemlist)
            {
                try
                {
                    var tempitem = referenceStorageable.GetNewInstance();
                    tempitem.ParseFromXElement(item);
                    list.Add(tempitem);
                }
                catch (Exception e) { logger.Fatal("Error parsing the element", e); }
            }
            sw.Stop();
            logger.Info(sw.Result(list.Count()));
            return list;
        }

        public async Task<bool> StorageableItemExists(Interfaces.IXMLStorageable referenceStorageable)
        {
            return await Task<bool>.Run(() => storageableItemExists(referenceStorageable));
        }

        private bool storageableItemExists(Interfaces.IXMLStorageable referenceStorageable)
        {
            var sw = new Performance.Stopwatch("storageableItemExists: " + referenceStorageable.XElementName);
            sw.Start();

            var isregistered = from registered in RegisteredAdditionalDBs where registered.XElementName == referenceStorageable.XElementName select registered;
            if (isregistered.Count() == 0)
                return false;

            XElement db = (from dbs in OtherDBs where dbs.Name == (referenceStorageable.XElementName + "s") select dbs).First();

            if (!db.AncestorsAndSelf(referenceStorageable.XElementName + "s").Any())
                db.Add(new XElement(referenceStorageable.XElementName + "s"));

            if (db.Elements(referenceStorageable.XElementName).Any(x => x.Element(referenceStorageable.IDFieldName).Value == referenceStorageable.ID))
            {
                sw.Stop();
                logger.Info(sw.Result());
                return true;
            }
            else
            {
                sw.Stop();
                logger.Info(sw.Result());
                return false;
            }
        }

        public async Task<bool> SaveOrUpdateStorageableItem(Interfaces.IXMLStorageable StorageableItem)
        {
            return await Task<bool>.Run(()=> saveOrUpdateStorageableItem(StorageableItem));
        }

        private bool saveOrUpdateStorageableItem(Interfaces.IXMLStorageable StorageableItem)
        {
            var sw = new Performance.Stopwatch("saveOrUpdateStorageableItem: " + StorageableItem.XElementName + ", " + StorageableItem.ID);
            sw.Start();

            var isregistered = from registered in RegisteredAdditionalDBs where registered.XElementName == StorageableItem.XElementName select registered;
            if (isregistered.Count() == 0)
                return false;

            XElement db = (from dbs in OtherDBs where dbs.Name == (StorageableItem.XElementName + "s") select dbs).First();

            if (!db.AncestorsAndSelf(StorageableItem.XElementName + "s").Any())
                db.Add(new XElement(StorageableItem.XElementName + "s"));

            try
            {
                if (db.Elements(StorageableItem.XElementName).Any(x => x.Element(StorageableItem.IDFieldName).Value == StorageableItem.ID))
                    db.Elements(StorageableItem.XElementName).Single(x => x.Element(StorageableItem.IDFieldName).Value == StorageableItem.ID).ReplaceWith(StorageableItem.GetXElement());
                else
                    db.Add(StorageableItem.GetXElement());
            }
            catch (Exception e)
            {
                logger.Fatal("Error saving " + StorageableItem.XElementName, e);
            }

            try { File.WriteAllText(DatabasePath + CurrentCompany.CompanyID + "\\" + StorageableItem.XElementName + "s.xml", db.ToString()); }
            catch (Exception e) { logger.Fatal("Error writing " + StorageableItem.XElementName + " to " + DatabasePath + CurrentCompany.CompanyID + "\\" + StorageableItem.XElementName + "s.xml", e); }

            sw.Stop();
            logger.Info(sw.Result());
            return true;
        }

        public async Task<bool> RegisterStorageableItem(Interfaces.IXMLStorageable StorageableItem)
        {
            return await Task<bool>.Run(() => registerStorageableItem(StorageableItem));
        }

        private bool registerStorageableItem(Interfaces.IXMLStorageable StorageableItem, bool insertInDatabase = true)
        {
            if (String.IsNullOrEmpty(StorageableItem.XElementName))
                return false;
            if (insertInDatabase)
                RegisteredAdditionalDBs.Add(StorageableItem);
            if (File.Exists(DatabasePath + CurrentCompany.CompanyID + "\\" + StorageableItem.XElementName + "s.xml"))
            {
                using (StreamReader reader = File.OpenText(DatabasePath + CurrentCompany.CompanyID + "\\" + StorageableItem.XElementName + "s.xml"))
                {
                    //Can Throw an exception if the file is empty
                    var db = XElement.Load(reader);
                    if (!db.AncestorsAndSelf(StorageableItem.XElementName + "s").Any())
                        return false;
                    OtherDBs.Add(db);
                    return true;
                }
            }
            else
            {
                using (StreamWriter writer = File.CreateText(DatabasePath + CurrentCompany.CompanyID + "\\" + StorageableItem.XElementName + "s.xml"))
                    writer.Write(new XElement(new XElement(StorageableItem.XElementName + "s")).ToString());
                using (StreamReader reader = File.OpenText(DatabasePath + CurrentCompany.CompanyID + "\\" + StorageableItem.XElementName + "s.xml"))
                {
                    var db = XElement.Load(reader);
                    if (!db.AncestorsAndSelf(StorageableItem.XElementName + "s").Any())
                        return false;
                    OtherDBs.Add(db);
                    return true;
                }
            }
        }


        public bool CanSync
        {
            get { return false; }
        }

        public Task<List<Interfaces.IStorageable>> GetUpdatedItems()
        {
            throw new NotImplementedException();
        }

        public Task<bool> CompareItem(Interfaces.IStorageable source)
        {
            throw new NotImplementedException();
        }

        public string GuID
        {
            get { return "538BAA9C-D630-486D-BD61-02706C09E2A9"; }
        }


        public string DatabaseTitle
        {
            get { return "XML Datenbank"; }
        }

        public string DatabaseDescription
        {
            get { return "Speichert alle Daten lokal auf Ihrer Festplatte im XML Format"; }
        }
    }
}
