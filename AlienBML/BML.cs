﻿// Alien Isolation (Binary XML converter)
// Written by WRS (xentax.com)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml;

namespace AlienBML
{
    // fake typedefs
    using u32 = UInt32;
    using u16 = UInt16;
    using u8 = Byte;

    class BML
    {
        // complete.
        struct Header
        {
            const string XML_FLAG = "xml\0";

            public u32 blockData { get; private set; }
            public u32 blockStrings { get; private set; }
            public u32 blockLineEndings { get; private set; }

            public bool Read(BinaryReader br)
            {
                bool valid = true;

                string magic = Encoding.Default.GetString(br.ReadBytes(XML_FLAG.Length));
                valid &= (magic == XML_FLAG);

                blockData = br.ReadUInt32();
                blockStrings = br.ReadUInt32();
                blockLineEndings = br.ReadUInt32();

                valid &= (blockLineEndings < br.BaseStream.Length);
                valid &= (blockStrings < blockLineEndings);
                valid &= (blockData < blockStrings);

                return valid;
            }

            public bool Write(BinaryWriter bw)
            {
                bool valid = true;

                bw.Write(Encoding.Default.GetBytes(XML_FLAG), 0, XML_FLAG.Length);

                valid &= blockLineEndings != 0;
                valid &= blockStrings != 0;
                valid &= blockData != 0;

                bw.Write(blockData);
                bw.Write(blockStrings);
                bw.Write(blockLineEndings);

                return valid;
            }

            public void Fixup(u32 of1, u32 of2, u32 of3)
            {
                blockData = of1;
                blockStrings = of2;
                blockLineEndings = of3;
            }

            static public u32 Size()
            {
                return 16;
            }
        }

        // complete.
        struct Attribute
        {
            public AlienString.Ref Name { get; private set; }
            public AlienString.Ref Value { get; private set; }

            public bool ReadXML(string str_name, string str_value)
            {
                Name = new AlienString.Ref(str_name, true);
                Value = new AlienString.Ref(str_value, true);

                return true;
            }

            public bool Read(BinaryReader br)
            {
                Name = new AlienString.Ref(br, true);
                Value = new AlienString.Ref(br, true);

                return true;
            }

            public bool Write(BinaryWriter bw, u32 pool_1_offset)
            {
                bool valid = true;

                // attributes are always from pool1
                Name.Fixup(pool_1_offset, 0);
                Value.Fixup(pool_1_offset, 0);

                bw.Write(Name.offset);
                bw.Write(Value.offset);

                return valid;
            }

            static public u32 Size()
            {
                return 8;
            }
        }

        // complete.
        class NodeFlags
        {
            public u8 Attributes { get; set; }
            public u8 Info { get; set; }
            public u16 Children { get; set; }

            public NodeFlags()
            {
                Attributes = 0;
                Info = 0;
                Children = 0;
            }

            public bool Read(BinaryReader br)
            {
                u32 bytes = br.ReadUInt32();

                // bit format:
                // aaaa aaaa iiic cccc cccc cccc cccc cccc

                // 8-bits : number of attributes
                Attributes = Convert.ToByte(bytes & 0xFF);

                // 3-bits : flags
                // xxxxx todo split up flags
                Info = Convert.ToByte((bytes >> 8) & 0x7);

                // 21-bits : number of child nodes
                u32 raw_children = (bytes >> 11) & 0x1FFFFF;

                // note: we store raw_children as u16 for alignment purposes
                // aaaa aaaa iiic cccc cccc cccc ccc- ---- (- = ignored)

#if DEBUG
                if (raw_children > 0xFFFF)
                {
                    Console.WriteLine("Warning: huge number of child nodes");
                }
#endif

                Children = Convert.ToUInt16(raw_children);

                return true;
            }

            public bool Write(BinaryWriter bw)
            {
                u32 bytes = 0;

                u32 tmp = Children;
                bytes |= tmp << 11;

                tmp = Info;
                bytes |= (tmp & 0x7) << 8;

                tmp = Attributes;
                bytes |= (tmp & 0xFF);

                bw.Write(bytes);

                return true;
            }

            static public u32 Size()
            {
                return 4;
            }
        }

        class Node
        {
            public List<Node> Nodes { get; private set; }
            public List<Attribute> Attributes { get; private set; }

            public AlienString.Ref End2 { get; private set; }
            public AlienString.Ref Text { get; private set; }
            public AlienString.Ref End { get; private set; }
            public AlienString.Ref Inner { get; private set; }

            public u32 Start { get; private set; }
            public u32 Offset { get; set; }

            public NodeFlags Flags { get; private set; }

            public Node()
            {
                Nodes = new List<Node>();
                Attributes = new List<Attribute>();
                Flags = new NodeFlags();
            }

            public void SetDeclaration()
            {
                Text = new AlienString.Ref("?xml", true);
                Flags.Children = 0;

                //End = new AlienString.Ref("\r\n", false);
            }

            // depth is used to create the spacing text
            public bool ReadXML(XmlElement ele, int depth = 0)
            {
                bool valid = true;

                Text = new AlienString.Ref(ele.Name, true);

                if (ele.HasAttributes)
                {
                    if( ele.Attributes.Count > 0xFF )
                    {
                        Console.WriteLine("Too many attributes for {0}", Text.value);
                        valid = false;
                        return valid;
                    }

                    foreach (XmlAttribute attr in ele.Attributes)
                    {
                        Attribute a = new Attribute();
                        a.ReadXML(attr.Name, attr.Value);
                        Attributes.Add(a);
                    }
                }

                if( ele.HasChildNodes )
                {
                    // inner text is treated as a special text node, so it has children.. (YIKES)

                    foreach( XmlNode xnode in ele.ChildNodes )
                    {
                        // special parser requirements

                        switch( xnode.NodeType )
                        {
                            case XmlNodeType.Element:

                                XmlElement child = (xnode as XmlElement);

                                Node nchild = new Node();

                                valid &= nchild.ReadXML(child, depth + 1);

                                if( valid )
                                {
                                    Nodes.Add(nchild);
                                }

                                break;

                            case XmlNodeType.Text:

                                Inner = new AlienString.Ref(xnode.Value,false);
                                End2 = new AlienString.Ref("\r\n", false);
                                
                                break;

                            default:
                                Console.WriteLine("Unhandled XML type");
                                valid = false;
                                break;
                        }
                    }
                }

                // bonus: whitespacing

                if( Nodes.Count > 0 )
                {
                    // include all indentation?
                    End = new AlienString.Ref("\r\n", false);
                }

                Fixup();

                return valid;
            }

            public bool Read(BinaryReader br)
            {
                bool valid = true;

                // we need this offset to tie nodes together
                Start = (u32)br.BaseStream.Position;

                Text = new AlienString.Ref(br, true);

                valid &= Flags.Read(br);
                
                // get attributes

                if( Flags.Attributes > 0 )
                {
#if DEBUG
                    if( Flags.Attributes > 100 )
                    {
                        Console.WriteLine("Possible large number of attributes -> {0} (node={1})", Flags.Attributes, Text);
                    }
#endif
                    for (u32 attribs = 0; attribs < (u32)Flags.Attributes; attribs++)
                    {
                        Attribute a = new Attribute();
                        valid &= a.Read(br);

                        if (!valid)
                        {
                            return false;
                        }

                        Attributes.Add(a);
                    }
                }

                switch (Flags.Info)
                {
                    case 0: // 000
                        // fake node
                        End = new AlienString.Ref(br, false);

                        break;

                    case 1: // 001
                        End = new AlienString.Ref(br, false);
                        Offset = br.ReadUInt32();

                        break;

                    case 2: // 010
                    case 6: // 110
                        End = new AlienString.Ref(br, false);

                        if (Flags.Children > 0)
                        {
                            Offset = br.ReadUInt32();
                        }

                        break;

                    case 3: // 011
                    case 7: // 111

                        // note: inner text is stored in the second pool

                        Inner = new AlienString.Ref(br, false); // inner text or line diff
                        End2 = new AlienString.Ref(br, false); // line ending

                        if (Flags.Children > 0)
                        {
                            Offset = br.ReadUInt32();
                        }

                        break;

                    default:
                        // flags may need sorting out
                        break;
                }

                return valid;
            }

            public u32 Size()
            {
                u32 my_size = 0;

                // text offset
                my_size += 4;
                // flags
                my_size += NodeFlags.Size();
                // attribute entries (read from flags)
                my_size += Attribute.Size() * Flags.Attributes;
                // check against info
                switch( Flags.Info )
                {
                    case 0:
                        my_size += 4;
                        break;
                    case 1:
                        my_size += 4 + 4;
                        break;
                    case 2:
                    case 6:
                        my_size += 4;
                        if (Flags.Children > 0) my_size += 4;
                        break;
                    case 3:
                    case 7:
                        my_size += 4 + 4;
                        if (Flags.Children > 0) my_size += 4;
                        break;
                    default:
                        Console.WriteLine("Unsupported info");
                        break;
                }

                return my_size;
            }

            public void Fixup()
            {
                Flags.Attributes = Convert.ToByte(Attributes.Count & 0xFF);
                Flags.Children = Convert.ToUInt16(Nodes.Count & 0xFFFF);

                if( Text.value == "?xml" )
                {
                    // both reference spacing
                    if (End == null)
                    {
                        End = new AlienString.Ref("\r\n", false);
                    }

                    if( Flags.Attributes == 0 )
                    {
                        // ignored
                        Flags.Info = 0;
                    }
                    else
                    {
                        // declaration kept - child mandatory
                        Flags.Info = 1;
                    }
                }
                else
                {
                    if( Inner != null )
                    {
                        // has inner kept; child optional
                        Flags.Info = 3; // or 7
                    }
                    else
                    {
                        // end spacing, child optional
                        Flags.Info = 2; // or 6
                    }
                }
            }

            public bool Write(BinaryWriter bw, u32 of1, u32 of2)
            {
                // text offset
                Text.Fixup(of1, of2);
                bw.Write(Text.offset);
                // flags
                Flags.Write(bw);
                foreach(Attribute a in Attributes)
                {
                    a.Write(bw, of1); // uh, we need to pass this in?
                }

                switch (Flags.Info)
                {
                    case 0:
                        bw.Write(Offset); // this needs to be resolved via Fixup xxxxxxxxx todo
                        break;
                    case 1:
                        End.Fixup(of1, of2);
                        bw.Write(End.offset); // this needs to be resolved via Fixup xxxxxxxxx todo
                        bw.Write(Offset); // this needs to be resolved via Fixup xxxxxxxxx todo
                        break;
                    case 2:
                    case 6:
                        End.Fixup(of1, of2);
                        bw.Write(End.offset); // this needs to be resolved via Fixup xxxxxxxxx todo
                        if (Flags.Children > 0) bw.Write(Offset); // this needs to be resolved via Fixup xxxxxxxxx todo
                        break;
                    case 3:
                    case 7:
                        Inner.Fixup(of1, of2);
                        End2.Fixup(of1, of2);
                        bw.Write(Inner.offset); // this needs to be resolved via Fixup xxxxxxxxx todo
                        bw.Write(End2.offset); // this needs to be resolved via Fixup xxxxxxxxx todo
                        if (Flags.Children > 0) bw.Write(Offset); // this needs to be resolved via Fixup xxxxxxxxx todo
                        break;
                    default:
                        Console.WriteLine("Unsupported info");
                        break;
                }

                return true;
            }
        }

        Header hdr;
        Node root;

        public BML()
        {
            root = new Node();
        }

        private bool ReadWrapper(BinaryReader br, ref Node owner)
        {
            bool success = true;

            Node n = new Node();
            success &= n.Read(br);

            // try to parse other blocks
            if( success )
            {
                if( n.Offset != 0 )
                {
                    long pos = br.BaseStream.Position;

                    // seek to child pos
                    br.BaseStream.Position = n.Offset;

                    for (u32 i = 0; i < n.Flags.Children; i++)
                    {
                        success &= ReadWrapper(br, ref n);
                    }

                    // seek back
                    br.BaseStream.Position = pos;
                }

                owner.Nodes.Add(n);
            }

            return success;
        }

        private bool ReadAllNodes(BinaryReader br)
        {
            bool success = root.Read(br);

            // this is always the initial node
            success &= (root.Text.value == "?xml");

            // there should be at least 1 child node
            success &= (root.Flags.Children > 0);

            if( !success )
            {
                Console.WriteLine("Unexpected XML data");
                return false;
            }
            
            success &= ReadWrapper(br, ref root);

            return success;
        }

        public bool ReadBML(BinaryReader br)
        {
            bool valid = true;

            valid &= hdr.Read(br);

            if (!valid)
            {
                Console.WriteLine("Failed to read header");
                return valid;
            }

            AlienString.StringPool1.Clear();
            AlienString.StringPool2.Clear();

            valid &= ReadAllNodes(br);

            return valid;
        }

        public bool ReadXML(BinaryReader br)
        {
            XmlDocument doc = new XmlDocument();

            try
            {
                doc.Load(br.BaseStream);
            }
            catch
            {
                Console.WriteLine("Failed to parse XML - exiting");
                return false;
            }

            bool valid = true;

            AlienString.StringPool1.Clear();
            AlienString.StringPool2.Clear();
            
            // fake setup for declaration (always root node)
            root.SetDeclaration();

            foreach (XmlNode xnode in doc.ChildNodes)
            {
                switch (xnode.NodeType)
                {
                    case XmlNodeType.XmlDeclaration:

                        XmlDeclaration decl = (xnode as XmlDeclaration);

                        // these are treated as root node attributes
                        // we also have to check the declaration has all of them

                        if (decl.Version != null && decl.Version.Length > 0)
                        {
                            Attribute ver = new Attribute();
                            ver.ReadXML("version", decl.Version);
                            root.Attributes.Add(ver);
                        }

                        if (decl.Encoding != null && decl.Encoding.Length > 0)
                        {
                            Attribute enc = new Attribute();
                            enc.ReadXML("encoding", decl.Encoding);
                            root.Attributes.Add(enc);
                        }

                        if (decl.Standalone != null && decl.Standalone.Length > 0)
                        {
                            Attribute sta = new Attribute();
                            sta.ReadXML("standalone", decl.Standalone);
                            root.Attributes.Add(sta);
                        }

                        root.Flags.Attributes = Convert.ToByte(root.Attributes.Count);
                        
                        break;

                    case XmlNodeType.Element:

                        Node actual_root = new Node();
                        valid &= actual_root.ReadXML(xnode as XmlElement);
                        root.Nodes.Add(actual_root);

                        break;

                    default:
                        Console.WriteLine("XmlNodeType not handled - skipping it");
                        break;
                }
            }

            return valid;
        }

        // xxxx todo refactor
        private string DumpNode(Node n, int depth = 0)
        {
            string d = "";
            bool ignored = (depth == 0 && n.Attributes.Count == 0 );
            
            if( !ignored )
            {
                d += String.Format("<{0}", n.Text.value);

                foreach (Attribute a in n.Attributes)
                {
                    d += String.Format(" {0}=\"{1}\"", a.Name.value, a.Value.value);
                }
            }

            if (n.Nodes.Count > 0)
            {
                if (!ignored)
                {
                    if( depth == 0 )
                    {
                        // first xml tag must end in matching <? tags ?>
                        d += "?";
                    }

                    d += ">";

                    if( n.End != null ) d += n.End.value;
                }

                if (n.Inner != null && n.Inner.value.Length != 0)
                {
                    d += n.Inner.value;
                }

                foreach (Node node in n.Nodes)
                {
                    d += DumpNode(node, depth + 1);
                }

                // uh, the first xml tag doesn't need to close
                if (depth != 0)
                {
                    d += String.Format("</{0}>", n.Text.value);

                    if( n.End2 != null ) d += n.End2.value;
                }
            }
            else if( !ignored )
            {
                if (n.Inner != null && n.Inner.value.Length != 0)
                {
                    d += ">";
                    d += n.Inner.value;
                    d += String.Format("</{0}>", n.Text.value);
                    d += n.End2.value;
                }
                else
                {
                    // <tag /> and <tag a="b" />
                    if( n.Attributes.Count > 0 )
                    {
                        d += " ";
                    }

                    d += "/>";

                    if (n.End != null) d += n.End.value;
                    if (n.End2 != null) d += n.End2.value;
                }
            }

            return d;
        }

        public bool ExportXML(ref string xml)
        {
            xml = DumpNode(root);

#if DEBUG
            Console.WriteLine(xml);
#endif

            return true;
        }

        u32 CalculateNodeSize(Node n)
        {
            // xxx remove this modifier
            n.Fixup();

            u32 size = n.Size();

            foreach(Node c in n.Nodes)
            {
                size += CalculateNodeSize(c);
            }

            return size;
        }

        // bit of a mess. we have to determine the offsets before exporting the nodes
        void ExportBMLNodes(BinaryWriter bw, Node n, u32 of1, u32 of2)
        {
            u32 first_child = (u32)bw.BaseStream.Position;

            // pass 1; get size at end of all these nodes
            foreach (Node c in n.Nodes)
            {
                first_child += c.Size();
            }

            // pass 2; write nodes and update local first_child offset
            foreach(Node c in n.Nodes)
            {
                c.Offset = first_child;
                c.Write(bw, of1, of2);

                foreach( Node subc in c.Nodes )
                {
                    first_child += subc.Size();
                }
            }

            // pass 2; write children
            foreach (Node c in n.Nodes)
            {
                ExportBMLNodes(bw, c, of1, of2);
            }
        }

        public bool ExportBML(BinaryWriter bw)
        {
            // pass 1: calculate file size

            u32 node_size = CalculateNodeSize(root);

            MemoryStream p1 = AlienString.StringPool1.Export();
            MemoryStream p2 = AlienString.StringPool2.Export();

            u32 block1 = Header.Size()
                + node_size
                + 1; // extra null byte

            u32 block2 = block1
                + 1 // extra null byte
                + (u32)p1.Length;

            u32 block3 = block2
                + (u32)p2.Length;

            u32 file_size = block3
                + 1; // extra null byte

            // sneak attack
            hdr.Fixup(block1, block2, block3);

            // pass 2: export this mess

            // header (16)
            // node data - followed by a null character
            // OFFSET 1 IS TAKEN FROM HERE
            // another null character
            // string pool 1
            // OFFSET 2 IS TAKEN FROM HERE
            // string pool 2
            // OFFSET 3 IS TAKEN FROM HERE
            // another null character

            bw.BaseStream.SetLength(file_size);

            u32 of1 = block1 + 1;
            u32 of2 = block2;

            // -- header
            hdr.Write(bw);
            // -- nodes (root is a slight exception)
            root.Offset = (u32)bw.BaseStream.Position + root.Size();
            root.Write(bw, of1, of2);
            ExportBMLNodes(bw, root, of1, of2);
            // -- string pools
            bw.BaseStream.Seek(block1 + 1, SeekOrigin.Begin);
            p1.WriteTo(bw.BaseStream);
            p2.WriteTo(bw.BaseStream);

            return true;
        }
    }
}
