//
// NAntContrib
// Copyright (C) 2002 Tomas Restrepo (tomasr@mvps.org)
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307 USA
//


using System;
using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Text;
using System.Web.Mail;
using SourceForge.NAnt.Attributes;
using SourceForge.NAnt;
using NAnt.Contrib.Util;

namespace NAnt.Contrib.Tasks
{

   /// <summary>
   /// A task to execute arbitrary SQL statements against a OLEDB data source.
   /// </summary>
   /// <remarks>
   /// You can specify a set of sql statements inside the
   /// sql element, or execute them from a text file that contains them. You can also
   /// choose to execute the statements in a single batch, or execute them one by one
   /// (even inside a transaction, if you want to).
   /// </remarks>
   /// <example>
   ///   <para>Execute a set of statements inside a transaction</para>
   ///   <code><![CDATA[
   ///      <sql
   ///         connstring="Provider=SQLOLEDB;Data Source=localhost; Initial Catalog=Pruebas; Integrated Security=SSPI"
   ///         transaction="true"
   ///         delimiter=";"
   ///      >
   ///         INSERT INTO jobs (job_desc, min_lvl, max_lvl) VALUES('My Job', 22, 45);
   ///         INSERT INTO jobs (job_desc, min_lvl, max_lvl) VALUES('Other Job', 09, 43);
   ///         SELECT * FROM jobs;
   ///      </sql>
   ///   ]]></code>
   /// </example>
   /// <example>
   ///   <para>Execute a set of statements from a file and
   ///         write all query results to a file</para>
   ///   <code><![CDATA[
   ///      <sql
   ///         connstring="Provider=SQLOLEDB;Data Source=localhost; Initial Catalog=Pruebas; Integrated Security=SSPI"
   ///         transaction="true"
   ///         delimiter=";"
   ///         print="true"
   ///         source="sql.txt"
   ///         output="${outputdir}/results.txt"
   ///      />
   ///   ]]></code>
   /// </example>
   /// <example>
   ///   <para>Execute a SQL script generated by SQL Server Enterprise Manager</para>
   ///   <code><![CDATA[
   ///      <sql
   ///         connstring="Provider=SQLOLEDB;Data Source=localhost; Initial Catalog=Pruebas; Integrated Security=SSPI"
   ///         transaction="true"
   ///         delimiter="GO"
   ///         delimstyle="Line"
   ///         print="true"
   ///         source="pubs.xml"
   ///         batch="false"
   ///         output="${outputdir}/results.txt"
   ///      />
   ///   ]]></code>
   /// </example>
   [ TaskName("sql") ]
   public class SqlTask : Task
   {
      private string _connectionString;
      private string _source;
      private string _delimiter;
      private DelimiterStyle _delimiterStyle = DelimiterStyle.Normal;
      private bool _print = false;
      private bool _useTransaction = true;
      private string _output;
      public string _embeddedSqlStatements;
      private bool _batch = true;
      TextWriter _outputWriter = null;
      private bool _expandProps = true;

      /// <summary>
      /// Connection string used to access database.
      /// This should be an OleDB connection string.
      /// </summary>
      [ TaskAttribute("connstring", Required=true) ]
      public string ConnectionString  {
         get { return _connectionString; }
         set { _connectionString = value; }
      }

      /// <summary>
      /// File where the sql statements are defined.
      /// </summary>
      /// <remarks>
      /// You cannot specify both a source and an inline set of statements
      /// </remarks>
      [ TaskAttribute("source") ]
      public string Source {
         get { return _source; }
         set { _source = value; }
      }

      /// <summary>
      /// String that separates statements from one another.
      /// </summary>
      [ TaskAttribute("delimiter", Required=true) ]
      public string Delimiter {
         get { return _delimiter; }
         set { _delimiter = value; }
      }

      /// <summary>
      /// If true, the statements will be executed as a single batch.
      /// If false, they will be executed one by one. Default is true.
      /// </summary>
      [ TaskAttribute("batch"), BooleanValidator ]
      public bool Batch {
         get { return _batch; }
         set { _batch = value; }
      }

      /// <summary>
      /// If true, the any nant-style properties on the sql will be
      /// expanded before execution. Default is true.
      /// </summary>
      [ TaskAttribute("expandprops"), BooleanValidator ]
      public bool ExpandProperties {
         get { return _expandProps; }
         set { _expandProps = value; }
      }

      /// <summary>
      /// Kind of delimiter used. Allowed values are Normal or Line.
      /// </summary>
      /// <remarks>
      /// Delimiters can be of two kinds: Normal delimiters are
      /// always specified inline, so they permit having two
      /// different statements in the same line. Line delimiters,
      /// however, need to be in a line by their own.
      /// Default is Normal.
      /// </remarks>
      [ TaskAttribute("delimstyle", Required=true) ]
      public DelimiterStyle DelimiterStyle {
         get { return _delimiterStyle; }
         set { _delimiterStyle = value; }
      }

      /// <summary>
      /// If set to true, results from the statements will be
      /// output to the build log.
      /// </summary>
      [ TaskAttribute("print"), BooleanValidator() ]
      public bool Print {
         get { return _print; }
         set { _print = value; }
      }

      /// <summary>
      /// If set, the results from the statements will be
      /// output to the specified file.
      /// </summary>
      [ TaskAttribute("output") ]
      public string Output {
         get { return _output; }
         set { _output = value; }
      }

      /// <summary>
      /// If set to true, all statements will be executed
      /// within a single transaction. Default value is true.
      /// </summary>
      [ TaskAttribute("transaction"), BooleanValidator() ]
      public bool UseTransaction {
         get { return _useTransaction; }
         set { _useTransaction = value; }
      }


      ///<summary>
      ///Initializes task and ensures the
      ///supplied attributes are valid.
      ///</summary>
      ///<param name="taskNode">Xml node used to define this task instance.</param>
      protected override void InitializeTask(System.Xml.XmlNode taskNode)
      {
         _embeddedSqlStatements = ((System.Xml.XmlElement)taskNode).InnerText;
         if ( (_embeddedSqlStatements=="") && (Source==null) )  {
            throw new BuildException("No source file or statements have been specified.", Location);
         }
      }

      /// <summary>
      /// This is where the work is done
      /// </summary>
      protected override void ExecuteTask()
      {
         bool closeWriter = false;

         if ( Output != null )  {
            try  {
               _outputWriter = new StreamWriter(File.OpenWrite(Output));
               closeWriter = true;
            }  catch ( IOException ioe )  {
               throw new BuildException("Cannot Open output file " + ioe.Message);
            }
         } else {
            _outputWriter = Console.Out;
         }

         StringBuilder message = new StringBuilder();
         message.Append ("SQL Task:" + Environment.NewLine);
         message.Append ("Connection String: " + ConnectionString + Environment.NewLine);
         message.Append ("Use Transaction?: " + UseTransaction.ToString() + Environment.NewLine);
         message.Append ("Batch Sql Statements?: " + this.Batch.ToString() + Environment.NewLine);
         message.Append ("Batch Delimiter: " + this.Delimiter + Environment.NewLine);
         message.Append ("Delimiter Style: " + this.DelimiterStyle.ToString() + Environment.NewLine);
         message.Append ("Fail On Error?: " + this.FailOnError.ToString() + Environment.NewLine);
         message.Append ("Source script file: " + this.Source + Environment.NewLine);
         message.Append ("Output file: " + this.Output + Environment.NewLine);

         Log.WriteLineIf(this.Verbose == true, message.ToString());

         if ( this.Verbose ) {
            _outputWriter.WriteLine(message.ToString());
         }

         SqlHelper sqlHelper = new SqlHelper(ConnectionString, UseTransaction);
         sqlHelper.Connection.InfoMessage += new OleDbInfoMessageEventHandler(SqlMessageHandler);

         try {
            if ( Batch )
               ExecuteStatementsInBatch(sqlHelper);
            else
               ExecuteStatements(sqlHelper);
         } catch ( Exception e ) {
            sqlHelper.Close(false);
            throw new BuildException("SQL Resulted in Exception: " + e.Message);
         }

         sqlHelper.Close(true);
         if ( closeWriter ) {
            _outputWriter.Close();
         }
      }


      /// <summary>
      /// Executes the SQL Statements one by one
      /// </summary>
      /// <param name="sqlHelper"></param>
      private void ExecuteStatements(SqlHelper sqlHelper)
      {
         SqlStatementList list = CreateStatementList();

         if ( Source == null ) {
            list.ParseSql(_embeddedSqlStatements);
         } else {
            list.ParseSqlFromFile(Source);
         }

         foreach ( string statement in list )
         {

            Log.WriteLineIf(this.Verbose, "SQL Statement:");
            Log.WriteLineIf(this.Verbose, statement);

            if ( this.Verbose ) {
               _outputWriter.WriteLine();
               _outputWriter.WriteLine("SQL Statement:");
               _outputWriter.WriteLine(statement);
            }

            IDataReader results = null;
            try {
               results = sqlHelper.Execute(statement);
            } catch (Exception error) {
               Log.WriteLine("SQL Error: " + error.Message);
               Log.WriteLine("Statement: " + statement);
            } finally {
               ProcessResults(results, _outputWriter);
            }

         }
      }

      /// <summary>
      /// Executes the SQL statements in a single batch
      /// </summary>
      /// <param name="sqlHelper"></param>
      private void ExecuteStatementsInBatch(SqlHelper sqlHelper)
      {
         SqlStatementList list = CreateStatementList();
         SqlStatementAdapter adapter
            = new SqlStatementAdapter(list);
         
         string sql = null;
         if ( Source == null ) {
            sql = adapter.AdaptSql(_embeddedSqlStatements);
         } else {
            sql = adapter.AdaptSqlFile(Source);
         }

         Log.WriteLineIf(this.Verbose, "SQL Statement:");
         Log.WriteLineIf(this.Verbose, sql);

         if ( this.Verbose ) {
            _outputWriter.WriteLine();
            _outputWriter.WriteLine("SQL Statement:");
            _outputWriter.WriteLine(sql);
         }

         IDataReader results = sqlHelper.Execute(sql);
         ProcessResults(results, _outputWriter);
      }

      /// <summary>
      /// Process a result set
      /// </summary>
      /// <param name="results">Result set</param>
      /// <param name="writer">TextWriter to write if print=true</param>
      private void ProcessResults(IDataReader results, TextWriter writer)
      {
         try 
         {
            do 
            {
               DataTable schema = results.GetSchemaTable();
               if ( schema != null )
               {
                  foreach ( DataRow row in schema.Rows ) {
                     writer.Write(row["ColumnName"].ToString() + "\t");
                  }
                  writer.WriteLine();
                  writer.WriteLine(new String('-', 79));
               }
               while ( results.Read() ) 
               {
                  for ( int i=0; i < results.FieldCount; i++ ) {
                     writer.Write(results[i].ToString() + "\t");
                  }
                  writer.WriteLine();
               }
               writer.WriteLine();

            } while ( results.NextResult() );

         } finally {
            results.Close();
         }
         if ( results.RecordsAffected >= 0 ) {
            Log.WriteLine(LogPrefix + "{0} records affected", results.RecordsAffected);
         }
      }


      private SqlStatementList CreateStatementList()
      {
         SqlStatementList list =
            new SqlStatementList(Delimiter, DelimiterStyle);
         
         if ( ExpandProperties ) {
            list.Properties = Project.Properties;
         }
         return list;
      }

      private void SqlMessageHandler(object sender, OleDbInfoMessageEventArgs e)
      {
         _outputWriter.WriteLine(e.Message);
         Log.WriteLineIf(this.Verbose, e.Message);
      }
   } // class SqlTask

} // namespace NAnt.Contrib.Tasks
