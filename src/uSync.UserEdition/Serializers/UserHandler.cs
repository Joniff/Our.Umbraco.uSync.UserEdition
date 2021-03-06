﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Jumoo.uSync.BackOffice;
using Jumoo.uSync.BackOffice.Helpers;
using Jumoo.uSync.Core;
using Jumoo.uSync.Core.Mappers;
using Our.Umbraco.uSync.UserEdition.UserOrganisation;
using Umbraco.Core;
using Umbraco.Core.Cache;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Models.Membership;
using Umbraco.Core.Persistence;
using Umbraco.Core.Services;
using uSync.UserEdition.Rdbms;
using uSync.UserEdition.Security;
using uSync.UserEdition.Xml;

namespace uSync.UserEdition.Serializers
{
	public class UserHandler : ISyncHandler
	{
		public string Name { get { return "uSync: UserHandler"; } }
		public int Priority { get { return Jumoo.uSync.BackOffice.uSyncConstants.Priority.Content + 2; } }
		public string SyncFolder { get { return "User"; } }

		private IUserService _userService;
		private IRuntimeCacheProvider _runtimeCacheProvider;
		private UserOrganisationService _userOrganisationService;
		private UmbracoDatabase Database;

		private const string NodeName = "User";
		private const string KeyAttribute = "Email";
		private const string OrganisationAttribute = "Organisation";
		private const string AliasAttribute = "Alias";
		private const string NameAttribute = "Name";
		private const string UserAttribute = "User";

		private const string CommentsNode = "Comments";
		private const string FailedPasswordAttemptsNode = "FailedPasswordAttempts";
		private const string GroupsNode = "Groups";
		private const string GroupNode = "Group";
		private const string IsApprovedNode = "IsApproved";
		private const string IsLockedOutNode = "IsLockedOut";
		private const string LastLockedOutDateNode = "LastLockedOutDate";
		private const string LastLoginDateNode = "LastLoginDate";
		private const string LastPasswordChangeDateNode = "LastPasswordChangeDate";
		private const string PasswordQuestionNode = "PasswordQuestion";
		private const string PasswordNode = "Password";
		private const string RawPasswordAnswerNode = "RawPasswordAnswer";
		private const string SecurityStampNode = "SecurityStamp";
		private const string SectionsNode = "Sections";

		private const string MemberTypeCacheKey = "544dd2b0-11d1-41b4-a346-3435dd2d80cf:";
		private const string FileExtension = "config";
		private const string FileFilter = "*." + FileExtension;

		public UserHandler()
		{
			_userService = ApplicationContext.Current.Services.UserService;
			_runtimeCacheProvider = ApplicationContext.Current.ApplicationCache.RuntimeCache;
			Database = ApplicationContext.Current.DatabaseContext.Database;
			_userOrganisationService = new UserOrganisationService(_userService);
		}

		private string Filename(IUser user) =>
			Umbraco.Core.IO.IOHelper.MapPath(uSyncBackOfficeContext.Instance.Configuration.Settings.Folder + "//" + SyncFolder + "//" +	user.Email.ToSafeFileName().Replace('.', '-') + "." + FileExtension);

		public void RegisterEvents()
		{
			UserService.SavedUser += UserService_SavedUser;
			UserService.DeletedUser += UserService_DeletedUser;
		}

		private void UserService_SavedUser(IUserService sender, Umbraco.Core.Events.SaveEventArgs<Umbraco.Core.Models.Membership.IUser> e)
		{
			if (uSyncEvents.Paused)
			{
				return;
			}
			foreach (var user in e.SavedEntities)
			{
				ExportUser(user);
			}
		}

		private void UserService_DeletedUser(IUserService sender, Umbraco.Core.Events.DeleteEventArgs<Umbraco.Core.Models.Membership.IUser> e)
		{
			if (uSyncEvents.Paused)
			{
				return;
			}
			foreach (var user in e.DeletedEntities)
			{
				uSyncIOHelper.ArchiveFile(Filename(user));
			}
		}
		
		private XElement CreateProperty(Property prop)
		{
			try
			{
				return prop.ToXml();
			}
			catch
			{
				return new XElement(prop.Alias, prop.Value);
			}
		}


		private XElement Serialize(IUser user)
		{
			var organisation = _userOrganisationService.GetOrganisation(user);
			var node = new XElement(NodeName,
				new XAttribute(KeyAttribute, user.Email),
				new XAttribute(OrganisationAttribute, organisation),
				new XAttribute(NameAttribute, user.Name),
				new XAttribute(UserAttribute, user.Username)
			);

			node.Add(new XElement(CommentsNode, user.Comments));
			node.Add(new XElement(FailedPasswordAttemptsNode, user.FailedPasswordAttempts));
			node.Add(new XElement(IsApprovedNode, user.IsApproved));
			node.Add(new XElement(IsLockedOutNode, user.IsLockedOut));
			node.Add(new XElement(LastLockedOutDateNode, user.LastLockoutDate));
			node.Add(new XElement(LastLoginDateNode, user.LastLoginDate));
			node.Add(new XElement(LastPasswordChangeDateNode, user.LastPasswordChangeDate));
			if (!string.IsNullOrWhiteSpace(user.PasswordQuestion))
			{
				node.Add(new XElement(PasswordQuestionNode, user.PasswordQuestion));
			}
			if (!string.IsNullOrWhiteSpace(user.RawPasswordAnswerValue))
			{
				node.Add(new XElement(RawPasswordAnswerNode, user.RawPasswordAnswerValue));
			}
			var securityGroups = System.Web.Security.Roles.GetRolesForUser(user.Username);
			if (securityGroups.Any())
			{
				var groups = new XElement(GroupsNode);
				foreach (var group in System.Web.Security.Roles.GetRolesForUser(user.Username))
				{
					groups.Add(new XElement(GroupNode, group));
				}
				node.Add(groups);
			}
			var cryptography = new Cryptography(user.Email + user.Name);
			node.Add(new XElement(PasswordNode, cryptography.Encrypt(user.RawPasswordValue)));

			//System.Diagnostics.Debug.WriteLine($"Password Write for {user.Email} = {user.RawPasswordValue}");

			node.Add(new XElement(SecurityStampNode, user.SecurityStamp));
			node.Add(new XElement(SectionsNode, string.Join(",", user.AllowedSections)));

			return node.Normalize();
		}

		private uSyncAction ExportUser(IUser user)
		{
			if (user == null)
			{
				return uSyncAction.Fail("User", typeof(IUser), "User not set");
			}

			try
			{
				var node = Serialize(user);
				var attempt = SyncAttempt<XElement>.Succeed(user.Email, node, typeof(IUser), ChangeType.Export);
				var filename = Filename(user);
				uSyncIOHelper.SaveNode(attempt.Item, filename);
				return uSyncActionHelper<XElement>.SetAction(attempt, filename);
			}
			catch (Exception ex)
			{
				LogHelper.Warn<UserHandler>($"Error saving User {user.Email}: {ex.Message}");
				return uSyncAction.Fail(user.Email, typeof(IUser), ChangeType.Export, ex);
			}
		}

		public IEnumerable<uSyncAction> ExportAll(string folder)
		{
			var actions = new List<uSyncAction>();
			try
			{
				var users = _userOrganisationService.GetUsers();
				foreach (var user in users)
				{
					actions.Add(ExportUser(user));
				}
			}
			catch (Exception ex)
			{
				LogHelper.Error<UserHandler>($"Export Failed ", ex);
			}
			return actions;
		}

		private string GetImportIds(PropertyType propType, string content)
		{
			var mapping = uSyncCoreContext.Instance.Configuration.Settings.ContentMappings
				.SingleOrDefault(x => x.EditorAlias == propType.PropertyEditorAlias);

			if (mapping != null)
			{
				LogHelper.Debug<Events>("Mapping Content Import: {0} {1}", () => mapping.EditorAlias, () => mapping.MappingType);

				IContentMapper mapper = ContentMapperFactory.GetMapper(mapping);

				if (mapper != null)
				{
					return mapper.GetImportValue(propType.DataTypeDefinitionId, content);
				}
			}

			return content;
		}

		private string GetImportXml(XElement parent)
		{
			var reader = parent.CreateReader();
			reader.MoveToContent();
			string xml = reader.ReadInnerXml();

			if (xml.StartsWith("<![CDATA["))
				return parent.Value;
			else
				return xml.Replace("&amp;", "&");
		}

		private bool? Deserialize(XElement node, bool force, out IUser user)
		{
			var email = node.Attribute(KeyAttribute);
			var name = node.Attribute(NameAttribute);
			var userName = node.Attribute(UserAttribute);
			var organisation = node.Attribute(OrganisationAttribute);
			user = null;

			if (email == null || name == null || userName == null || organisation == null)
			{
				LogHelper.Warn<UserHandler>($"Error reading {node.Document.BaseUri}");
				return null;
			}

			user = _userService.GetByEmail(email.Value);
			if (user == null)
			{
				user = _userOrganisationService.CreateUser(userName.Value, email.Value, organisation.Value);
			}
			else if (_userOrganisationService.GetOrganisation(user) != organisation.Value)
			{
				_userOrganisationService.SetOrganisation(user, organisation.Value);
			}

			var groups = new List<string>();
			string password = null;
			foreach (var el in node.Elements())
			{
				switch (el.Name.LocalName)
				{
					case CommentsNode:
						user.Comments = el.Value;
						break;

					case FailedPasswordAttemptsNode:
						user.FailedPasswordAttempts = int.Parse(el.Value);
						break;

					case GroupsNode:
						foreach (var group in el.Elements())
						{
							groups.Add(el.Value);
						}
						break;

					case IsApprovedNode:
						user.IsApproved = bool.Parse(el.Value);
						break;

					case IsLockedOutNode:
						user.IsLockedOut = bool.Parse(el.Value);
						break;

					case LastLockedOutDateNode:
						user.LastLockoutDate = DateTime.Parse(el.Value);
						break;

					case LastLoginDateNode:
						user.LastLoginDate = DateTime.Parse(el.Value);
						break;

					case LastPasswordChangeDateNode:
						user.LastPasswordChangeDate = DateTime.Parse(el.Value);
						break;

					case PasswordQuestionNode:
						user.PasswordQuestion = el.Value;
						break;

					case PasswordNode:
						password = el.Value;
						break;

					case RawPasswordAnswerNode:
						user.RawPasswordAnswerValue = el.Value;
						break;

					case SectionsNode:
						foreach (var section in el.Value.Split(','))
						{
							user.AddAllowedSection(section);
						}
						break;

					case SecurityStampNode:
						user.SecurityStamp = el.Value;
						break;

				}
			}

			if (password != null)
			{
				var cryptography = new Cryptography(user.Email + user.Name);
				user.RawPasswordValue = password = cryptography.Decrypt(password);
				//System.Diagnostics.Debug.WriteLine($"Password Read for {user.Email} = {user.RawPasswordValue}");
			}

			_userService.Save(user, true);

			if (password != null)
			{
				//	Double check password is in database
				var userDto = Database.SingleOrDefault<UserDto>(user.Id);
				if (userDto == null)
				{
					LogHelper.Warn<UserHandler>($"Member {user.Id} doesn\'t exist in table umbracoUser even after we have just saved it");
				}
				else if (userDto.Password != password)
				{
					userDto.Password = password;
					Database.Update(userDto);
				}
			}

			return true;
		}

		private IEnumerable<uSyncAction> ImportFile(string file, bool force)
		{
			var actions = new List<uSyncAction>();
			var xml = XDocument.Load(file);

			foreach (var el in xml.Nodes().Where(x => x.NodeType == System.Xml.XmlNodeType.Element).Cast<XElement>().Where(x => x.Name == NodeName))
			{
				IUser user;
				var success = Deserialize(el, force, out user);
				if (success == true)
				{
					actions.Add(uSyncActionHelper<IUser>.SetAction(SyncAttempt<IUser>.Succeed(user.Email, user, ChangeType.Import), file));
				}
				else if (success == false)
				{
					actions.Add(uSyncActionHelper<IUser>.SetAction(SyncAttempt<IUser>.Fail(user.Email, user, ChangeType.Import), file));
				}
				else   // Must be null
				{
					//	We can't import this member, as its of a different type, silently ignore
				}
			}
			return actions;
		}

		public IEnumerable<uSyncAction> ImportAll(string folder, bool force)
		{
			try
			{
				var actions = new List<uSyncAction>();
				foreach (var file in Directory.GetFiles(Umbraco.Core.IO.IOHelper.MapPath(folder), FileFilter))
				{
					actions.AddRange(ImportFile(file, force));
				}

				return actions;
			}
			catch (Exception ex)
			{
				LogHelper.Error<UserHandler>($"Import Failed ", ex);
			}
			return Enumerable.Empty<uSyncAction>();
		}

		// Return True, if the element matches an existing user with same properties, false if they don't match or don't exist, null if we can't import this because of different types
		private bool? CompareUser(XElement node)
		{
			var email = node.Attribute(KeyAttribute);
			var name = node.Attribute(NameAttribute);
			var user = node.Attribute(UserAttribute);
			var organisation = node.Attribute(OrganisationAttribute);

			if (email == null || name == null || user == null || organisation == null)
			{
				LogHelper.Warn<UserHandler>($"Error reading {node.ToString()}");
				return null;
			}

			var existingUser = _userService.GetByEmail(email.Value);

			if (existingUser == null || name.Value != existingUser.Name || user.Value != existingUser.Username)
			{
				return false;
			}

			var compare = Serialize(existingUser);

			return XNode.DeepEquals(node.Normalize(), compare);
		}

		public IEnumerable<uSyncAction> ReportFile(string file)
		{
			var actions = new List<uSyncAction>();
			var xml = XDocument.Load(file);

			foreach (var el in xml.Nodes().Where(x => x.NodeType == System.Xml.XmlNodeType.Element).Cast<XElement>().Where(x => x.Name == NodeName))
			{
				actions.Add(uSyncActionHelper<IUser>.ReportAction(CompareUser(el) == false, el.Attribute(KeyAttribute).Value));
			}
			return actions;
		}

		public IEnumerable<uSyncAction> ReportFolder(string folder)
		{
			var actions = new List<uSyncAction>();
			if (Directory.Exists(folder))
			{
				foreach (var file in Directory.GetFiles(folder, FileFilter))
				{
					actions.AddRange(ReportFile(file));
				}

				foreach (var child in Directory.GetDirectories(folder))
				{
					actions.AddRange(ReportFolder(child));
				}
			}

			return actions;
		}

		public IEnumerable<uSyncAction> Report(string folder)
		{
			try
			{
				return ReportFolder(Umbraco.Core.IO.IOHelper.MapPath(folder));
			}
			catch (Exception ex)
			{
				LogHelper.Error<UserHandler>($"Report Failed ", ex);
			}
			return Enumerable.Empty<uSyncAction>();

		}
	}
}
