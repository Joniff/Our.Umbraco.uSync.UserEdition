using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Umbraco.Core.Models.Membership;
using Umbraco.Core.Services;

namespace Our.Umbraco.uSync.UserEdition.UserOrganisation
{
	public class UserOrganisationService
	{
		private enum ImplemtationType
		{
			Unknown = 0,
			NotImplemented,
			UsesIUserType,
			UsesIUserGroup
		}

		private static ImplemtationType Implemtation = ImplemtationType.Unknown;
		private IUserService userService = null;
		private char GroupSeparator = '.';

		private MethodInfo ReadAllOrganisationsMethod;
		private MethodInfo createUserMethod;
		private MethodInfo getUserTypeByAliasMethod;
		private MethodInfo getUserGroupByAliasMethod;

		private string GetAlias(object source) => source.GetType().GetProperty("Alias").GetValue(source) as string;
		private object GetUserTypeByAlias(string organisation) => getUserTypeByAliasMethod.Invoke(userService, new object[] {organisation});
		private object GetUserGroupByAlias(string organisation) => getUserGroupByAliasMethod.Invoke(userService, new object[] {organisation});

		public UserOrganisationService(IUserService us = null)
		{
			userService = us;
			if (userService == null)
			{
				userService = global::Umbraco.Core.ApplicationContext.Current.Services.UserService;
			}
			
			if (Implemtation == ImplemtationType.Unknown)
			{
				Implemtation = ImplemtationType.NotImplemented;
				
				ReadAllOrganisationsMethod = userService.GetType().GetMethod("GetAllUserTypes");
				if (ReadAllOrganisationsMethod != null)
				{
					createUserMethod = userService.GetType().GetMethod("CreateUserWithIdentity");
					getUserTypeByAliasMethod = userService.GetType().GetMethod("GetUserTypeByAlias");
					if (createUserMethod != null && getUserTypeByAliasMethod != null)
					{
						Implemtation = ImplemtationType.UsesIUserType;
					}
				}
				else
				{
					ReadAllOrganisationsMethod = userService.GetType().GetMethod("GetAllUserGroups");
					if (ReadAllOrganisationsMethod != null)
					{
						createUserMethod = userService.GetType().GetMethod("CreateUserWithIdentity");
						getUserGroupByAliasMethod = userService.GetType().GetMethod("GetUserGroupByAlias");
						if (createUserMethod != null && getUserGroupByAliasMethod != null)
						{
							Implemtation = ImplemtationType.UsesIUserGroup;
						}
					}
				}
			}
		}

		public IUser CreateUser(string name, string email, string organisation)
		{
			switch (Implemtation)
			{
				case ImplemtationType.UsesIUserType:
					return createUserMethod.Invoke(userService, new object[] {name, email, GetUserTypeByAlias(organisation)}) as IUser;

				case ImplemtationType.UsesIUserGroup:
					var user = createUserMethod.Invoke(userService, new object[] {name, email}) as IUser;
					SetOrganisation(user, organisation);
					return user;
			}

			return null;
		}

		public bool SetOrganisation(IUser user, string organisation)
		{
			switch (Implemtation)
			{
				case ImplemtationType.UsesIUserType:
					var userTypeProperty = user.GetType().GetProperty("UserType");
					var userType = GetUserTypeByAlias(organisation);

					userTypeProperty.SetValue(user, userType);
					return true;

				case ImplemtationType.UsesIUserGroup:
					var addGroupMethod = user.GetType().GetMethod("AddGroup");
					var orgs = organisation.Split(GroupSeparator);
					foreach (var org in orgs)
					{
						var group = GetUserGroupByAlias(org);
						if (group != null)
						{
							addGroupMethod.Invoke(user, new object[] {group});
						}
					}
					return true;
			}

			return false;
		}

		public string GetOrganisation(IUser user)
		{
			switch (Implemtation)
			{
				case ImplemtationType.UsesIUserType:
					var userTypeProperty = user.GetType().GetProperty("UserType");
					var userType = userTypeProperty.GetValue(user);
					var aliasProperty = userType.GetType().GetProperty("Alias");
					return aliasProperty.GetValue(userType) as string;

				case ImplemtationType.UsesIUserGroup:
					var groupProperty = user.GetType().GetProperty("Groups");
					var groups = groupProperty.GetValue(user) as IEnumerable<object>;
					var output = new StringBuilder();
					foreach (var group in groups)
					{
						if (output.Length != 0)
						{
							output.Append(GroupSeparator);
						}
						output.Append(GetAlias(group));
					}

					return output.ToString();
			}

			return null;
		}

		public IEnumerable<string> GetOrganisations()
		{
			var results = new List<string>();
			var orgs = ReadAllOrganisationsMethod.Invoke(userService, new object[] { new int[0] }) as IEnumerable<object>;
			foreach (var org in orgs)
			{
				results.Add(GetAlias(org));
			}
			return results;
		}

		public IEnumerable<IUser> GetUsers()
		{
			int totalRecords = 0;
			return userService.GetAll(0, 100000, out totalRecords);
		}

	}
}
