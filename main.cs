using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Threading;
using System.Xml.Serialization;
using System.Xml;
using Gtk;
using Gecko;

class MsdnView : Window {
	NodeStore Store;
	
	public MsdnView () : base ("Msdn View")
	{	
		DefaultSize = new Gdk.Size (1024,1024);

		HPaned hb = new HPaned ();
	
		Store = new NodeStore (typeof (TreeNode));
		WebControl wc = new WebControl ();
		ScrolledWindow sw = new ScrolledWindow ();
		NodeView view = new NodeView (Store);	
		view.HeadersVisible = false;
		view.AppendColumn ("Name", new CellRendererText (), "text", 0);	
		sw.WidthRequest = 300;
		InitTree ();
		Add (hb);
		hb.Add (sw);
		hb.Add (wc);
		sw.Add (view);
		
		// Events
		DeleteEvent += delegate {
			Application.Quit ();
		};

		view.NodeSelection.Changed += delegate {
			TreeNode n = (TreeNode) view.NodeSelection.SelectedNode;
			if (n == null)
				return;
			
			//
			// Fool msdn's code that tries to detect if it
			// is in a frame
			//
			string html = @"
<frameset>
  <frame src='" + n.Href + @"?frame=true' />
</frameset>";
			
			wc.OpenStream (MsdnClient.BaseUrl, "text/html");
			wc.AppendData (html);
			wc.CloseStream ();

		};
		view.RowExpanded += delegate (object o, RowExpandedArgs args) {
			TreeNode n = (TreeNode) Store.GetNode (args.Path);
			n.PopulateChildrenAsync ();
		};
	}

	void InitTree ()
	{
		Tree t = Tree.GetDefault ();
		
		foreach (TreeNode n in t.Children) {
			Store.AddNode (n);
			n.PopulateGuiChildren ();
		}
	}

	static void Main ()
	{
		Application.Init ();
	
		new MsdnView ().ShowAll ();
	
		Application.Run ();
	}
}

static class MsdnClient {
	static readonly XmlSerializer [] serializers = XmlSerializer.FromTypes (new Type [] {typeof (Tree), typeof (TreeNode)});
	static readonly XmlSerializer tree_ser = serializers [0];
	static readonly XmlSerializer node_ser = serializers [1];

#if USE_WHIDBEY
	// This is used by the dexplore.exe browser in whidbey beta
	// 2. however, it seems to be extremely broken. Maybe they
	// will fix it by RTM ;-).
	public const string BaseUrl = "http://whidbey.msdn.microsoft.com";
	public const string TopXml = "/library/en-us/toc/msdnlibWhidbeytest/top.xml";
#elif USE_WINFX
	// Avalon, Indigo, Longhorn (or whatever lame names they got)'
	// Sadly, because msft does not like to program for firefox,
	// these don't actually do too much :-(. 
	public const string BaseUrl = "http://winfx.msdn.microsoft.com";
	public const string TopXml = "/library/en-us/toc/winfxsdk/top.xml";
#else 
	// Standard msdn
	public const string BaseUrl = "http://msdn.microsoft.com";
	public const string TopXml = "/library/en-us/toc/msdnlib/top.xml";
#endif

	public static Stream OpenRead (string s)
	{
		// Pretend you are using a 56k connection ;-)
		// Thread.Sleep (1000);

		WebClient wc = new WebClient ();
		wc.BaseAddress = BaseUrl;
		return wc.OpenRead (s);
	}

	public static Tree OpenTree (string s)
	{
		return (Tree) tree_ser.Deserialize (MsdnClient.OpenRead (s));
	}
	
	public static TreeNode OpenTreeNode (string s)
	{
		return (TreeNode) node_ser.Deserialize (MsdnClient.OpenRead (s));
	}
}

public class DummyNode : TreeNode {
	public DummyNode () {
		Title = "Loading...";
	}
}

public class Tree : TreeNode {
	public static Tree GetDefault ()
	{
		return MsdnClient.OpenTree (MsdnClient.TopXml);
	}
}

public class TreeNode : Gtk.TreeNode {
	[XmlAttribute]
	public string NodeId;

	[XmlAttribute]
	[TreeNodeValue (Column=0)]
	public string Title;
	
	[XmlAttribute]
	public string Href;
	
	[XmlAttribute]
	public string ParentXmlSrc;
	
	[XmlAttribute]
	public string NodeXmlSrc;
    
	TreeNode [] children;

	[XmlElement ("TreeNode"), XmlElement ("Tree", typeof (Tree))]
	public TreeNode [] Children {
		get {
			return children;
		}
		set {
			if (value == null)
				return;
			ArrayList ar = new ArrayList (value.Length);
			Flatten (ar, value);
			children = (TreeNode []) ar.ToArray (typeof (TreeNode));
		}
	}
	
	static void Flatten (ArrayList ar, TreeNode [] nodes)
	{
		foreach (TreeNode n in nodes) {
			if (n is Tree) {
				// Trees always seem to have nodes
				// included in the xml, so I am not
				// sure if the populaltion is
				// necessary. But let's be safe
				n.PopulateChildrenData ();
				Flatten (ar, n.Children);
			} else
				ar.Add (n);
		}
	}

	public void PopulateChildrenData ()
	{
		if (Children != null || NodeXmlSrc == null)
			return;
		
		TreeNode n;
		if (this is Tree)
			// I've never seen this, but just in case...
			n = MsdnClient.OpenTree (NodeXmlSrc);
		else
			n = MsdnClient.OpenTreeNode (NodeXmlSrc);
		
		Children = n.Children;
	}
	
	bool populating;
	public void PopulateChildrenAsync ()
	{
		// Fastpath filled ones
		if (Children != null || NodeXmlSrc == null)
			return;

		// Prevent race conditions. We don't need a CAS here
		// because the population can only start from one
		// thread. The issue is that this can be re-entered
		// before we are done populating.
		if (populating)
			return;
		populating = true;

		ThreadPool.QueueUserWorkItem (delegate {
			PopulateChildrenData ();
			
			Application.Invoke (delegate {
				PopulateGuiChildren ();
			});
		});
	}
	
	public void PopulateGuiChildren () 
	{
		if (Children != null) {
			foreach (TreeNode c in Children) {
				AddChild (c);
				c.PopulateGuiChildren ();
			}
	
			if (this [0] is DummyNode)
				this.RemoveChild (this [0] as DummyNode);
		} else if (NodeXmlSrc != null && ChildCount == 0)
			AddChild (new DummyNode ());
	}
    
	public override string ToString ()
	{
		return string.Format ("TreeNode [{0}:{1}]", NodeId, Title);
	}

	public string ToStringRecurse ()
	{
		string s = this.ToString ();
		if (Children != null) {
			s += "\n{\n";
			foreach (TreeNode c in Children)
				s += c.ToStringRecurse () + "\n";
			s += "}\n";
		}
		return s;
	}
}
