﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace BLToolkit.Data.Linq.Parser
{
	using BLToolkit.Linq;
	using Data.Sql;
	using Reflection;

	// This class implements double functionality (scalar and member type selects)
	// and could be implemented as two different classes.
	// But the class means to have a lot of inheritors, and functionality of the inheritors
	// will be doubled as well. So lets double it once here.
	//
	public class SelectContext : IParseContext
	{
		#region Init

		public IParseContext[]  Sequence { get; set; }
		public LambdaExpression Lambda   { get; set; }
		public Expression       Body     { get; set; }
		public ExpressionParser Parser   { get; private set; }
		public SqlQuery         SqlQuery { get; set; }
		public IParseContext    Parent   { get; set; }
		public bool             IsScalar { get; private set; }

		Expression IParseContext.Expression { get { return Lambda; } }

		bool _isGroupBySubquery;

		public readonly Dictionary<MemberInfo,Expression> Members = new Dictionary<MemberInfo,Expression>();

		public SelectContext(IParseContext parent, LambdaExpression lambda, params IParseContext[] sequences)
		{
			Parent   = parent;
			Sequence = sequences;
			Parser   = sequences[0].Parser;
			Lambda   = lambda;
			Body     = lambda.Body.Unwrap();
			SqlQuery = sequences[0].SqlQuery;

			foreach (var context in Sequence)
				context.Parent = this;

			switch (Body.NodeType)
			{
				// .Select(p => new { ... })
				//
				case ExpressionType.New        :
					{
						var expr = (NewExpression)Body;

// ReSharper disable ConditionIsAlwaysTrueOrFalse
// ReSharper disable HeuristicUnreachableCode
						if (expr.Members == null)
							return;
// ReSharper restore HeuristicUnreachableCode
// ReSharper restore ConditionIsAlwaysTrueOrFalse

						for (var i = 0; i < expr.Members.Count; i++)
						{
							var member = expr.Members[i];

							Members.Add(member, expr.Arguments[i]);

							if (member is MethodInfo)
								Members.Add(TypeHelper.GetPropertyByMethod((MethodInfo)member), expr.Arguments[i]);
						}

						break;
					}

				// .Select(p => new MyObject { ... })
				//
				case ExpressionType.MemberInit :
					{
						var expr = (MemberInitExpression)Body;

						foreach (var binding in expr.Bindings)
						{
							if (binding is MemberAssignment)
							{
								var ma = (MemberAssignment)binding;

								Members.Add(binding.Member, ma.Expression);

								if (binding.Member is MethodInfo)
									Members.Add(TypeHelper.GetPropertyByMethod((MethodInfo)binding.Member), ma.Expression);
							}
							else
								throw new InvalidOperationException();
						}

						_isGroupBySubquery =
							Body.Type.IsGenericType &&
							Body.Type.GetGenericTypeDefinition() == typeof(ExpressionParser.GroupSubQuery<,>);

						break;
					}

				// .Select(p => everything else)
				//
				default                        :
					IsScalar = true;
					break;
			}
		}

		#endregion

		#region BuildQuery

		public virtual void BuildQuery<T>(Query<T> query, ParameterExpression queryParameter)
		{
			var expr = BuildExpression(null, 0);

			var mapper = Expression.Lambda<Func<QueryContext,IDataContext,IDataReader,Expression,object[],T>>(
				expr, new []
				{
					ExpressionParser.ContextParam,
					ExpressionParser.DataContextParam,
					ExpressionParser.DataReaderParam,
					ExpressionParser.ExpressionParam,
					ExpressionParser.ParametersParam,
				});

			query.SetQuery(mapper.Compile());
		}

		#endregion

		#region BuildExpression

		public virtual Expression BuildExpression(Expression expression, int level)
		{
			if (expression == null)
				return Parser.BuildExpression(this, Body);

			var levelExpression = expression.GetLevelExpression(level);

			if (IsScalar)
			{
				if (Body.NodeType != ExpressionType.Parameter && level == 0)
					if (levelExpression == expression)
						if (IsSubQuery() && IsExpression(null, 0, RequestFor.Expression))
						{
							var info = ConvertToIndex(expression, level, ConvertFlags.Field).Single();
							var idx = Parent == null ? info.Index : Parent.ConvertToParentIndex(info.Index, this);

							return Parser.BuildSql(expression.Type, idx);
						}

				return ProcessScalar(
					expression,
					level,
					(ctx, ex, l) => ctx.BuildExpression(ex, l),
					() => GetSequence(expression, level).BuildExpression(null, 0));
			}
			else
			{
				var sequence  = GetSequence(expression, level);
				var parameter = Lambda.Parameters[Sequence.Length == 0 ? 0 : Array.IndexOf(Sequence, sequence)];

				if (level == 0)
					return levelExpression == expression ?
						sequence.BuildExpression(null,       0) :
						sequence.BuildExpression(expression, level + 1);

				switch (levelExpression.NodeType)
				{
					case ExpressionType.MemberAccess :
						{
							var memberExpression = Members[((MemberExpression)levelExpression).Member];

							if (levelExpression == expression)
							{
								if (IsSubQuery())
								{
									switch (memberExpression.NodeType)
									{
										case ExpressionType.New        :
										case ExpressionType.MemberInit :
											return memberExpression.Convert(e =>
											{
												if (e != memberExpression)
												{
													if (!sequence.IsExpression(e, 0, RequestFor.Object) &&
													    !sequence.IsExpression(e, 0, RequestFor.Field))
													{
														var info = ConvertToIndex(e, 0, ConvertFlags.Field).Single();
														var idx  = Parent == null ? info.Index : Parent.ConvertToParentIndex(info.Index, this);

														return Parser.BuildSql(e.Type, idx);
													}

													return Parser.BuildExpression(this, e);
												}

												return e;
											});
									}

									if (!sequence.IsExpression(memberExpression, 0, RequestFor.Object) &&
									    !sequence.IsExpression(memberExpression, 0, RequestFor.Field))
									{
										var info = ConvertToIndex(expression, level, ConvertFlags.Field).Single();
										var idx  = Parent == null ? info.Index : Parent.ConvertToParentIndex(info.Index, this);

										return Parser.BuildSql(expression.Type, idx);
									}
								}

								return Parser.BuildExpression(this, memberExpression);
							}

							switch (memberExpression.NodeType)
							{
								case ExpressionType.Parameter  :
									if (memberExpression == parameter)
										return sequence.BuildExpression(expression, level + 1);
									break;

								case ExpressionType.New        :
								case ExpressionType.MemberInit :
									{
										var mmExpresion = GetMemberExpression(memberExpression, expression, level + 1);
										return BuildExpression(mmExpresion, 0);
									}
							}

							var expr = expression.Convert(ex => ex == levelExpression ? memberExpression : ex);

							return sequence.BuildExpression(expr, 1);
						}

					case ExpressionType.Parameter :

						//if (levelExpression == expression)
							break;
						//return Sequence.BuildExpression(expression, level + 1);
				}
			}

			throw new NotImplementedException();
		}

		#endregion

		#region ConvertToSql

		readonly Dictionary<MemberInfo,SqlInfo[]> _sql = new Dictionary<MemberInfo,SqlInfo[]>();

		public virtual SqlInfo[] ConvertToSql(Expression expression, int level, ConvertFlags flags)
		{
			if (IsScalar)
			{
				if (expression == null)
					return Parser.ParseExpressions(this, Body, flags);

				switch (flags)
				{
					case ConvertFlags.Field :
					case ConvertFlags.Key   :
					case ConvertFlags.All   :
						{
							if (Body.NodeType != ExpressionType.Parameter && level == 0)
							{
								var levelExpression = expression.GetLevelExpression(level);

								if (levelExpression != expression)
									if (flags != ConvertFlags.Field && IsExpression(expression, level, RequestFor.Field))
										flags = ConvertFlags.Field;
							}

							return ProcessScalar(
								expression,
								level,
								(ctx, ex, l) => ctx.ConvertToSql(ex, l, flags),
								() => new[] { new SqlInfo { Sql = Parser.ParseExpression(this, expression) } });
						}
				}
			}
			else
			{
				if (expression == null)
				{
					if (flags != ConvertFlags.Field)
					{
						var q =
							from m in Members
							where !(m.Key is MethodInfo)
							select ConvertMember(m.Value, flags) into mm
							from m in mm
							select m;

						return q.ToArray();
					}

					throw new NotImplementedException();
				}

				switch (flags)
				{
					case ConvertFlags.All   :
					case ConvertFlags.Key   :
					case ConvertFlags.Field :
						{
							var levelExpression = expression.GetLevelExpression(level);

							switch (levelExpression.NodeType)
							{
								case ExpressionType.MemberAccess :
									{
										if (level != 0 && levelExpression == expression)
										{
											var member = ((MemberExpression)levelExpression).Member;

											SqlInfo[] sql;

											if (!_sql.TryGetValue(member, out sql))
											{
												sql = ParseExpressions(Members[member], flags);
												_sql.Add(member, sql);
											}

											return sql;
										}

										return ProcessMemberAccess(
											expression, levelExpression, level,
											(n,ctx,ex,l,mex) =>
											{
												switch (n)
												{
													case 0 :
													//case 2 :
														var parseExpression = GetParseExpression(expression, levelExpression, mex);
														return ParseExpressions(parseExpression, flags);
													default:
														return ctx.ConvertToSql(ex, l, flags);
												}
											});
									}

								case ExpressionType.Parameter:
									if (levelExpression != expression)
										return GetSequence(expression, level).ConvertToSql(expression, level + 1, flags);

									if (level == 0)
										return GetSequence(expression, level).ConvertToSql(null, 0, flags);

									break;
							}

							break;
						}
				}
			}

			throw new NotImplementedException();
		}

		SqlInfo[] ConvertMember(Expression expression, ConvertFlags flags)
		{
			switch (expression.NodeType)
			{
				case ExpressionType.MemberAccess :
				case ExpressionType.Parameter :
					if (IsExpression(expression, 0, RequestFor.Field))
						flags = ConvertFlags.Field;
					return ConvertToSql(expression, 0, flags);
			}

			return ParseExpressions(expression, flags);
		}

		SqlInfo[] ParseExpressions(Expression expression, ConvertFlags flags)
		{
			return Parser.ParseExpressions(this, expression, flags)
				.Select(_ => CheckExpression(_))
				.ToArray();
		}

		SqlInfo CheckExpression(SqlInfo expression)
		{
			if (expression.Sql is SqlQuery.SearchCondition)
			{
				expression.Sql = Parser.Convert(this, new SqlFunction(typeof(bool), "CASE", expression.Sql, new SqlValue(true), new SqlValue(false)));
			}

			return expression;
		}

		#endregion

		#region ConvertToIndex

		readonly Dictionary<Tuple<MemberInfo,ConvertFlags>,SqlInfo[]> _memberIndex = new Dictionary<Tuple<MemberInfo,ConvertFlags>,SqlInfo[]>();

		public virtual SqlInfo[] ConvertToIndex(Expression expression, int level, ConvertFlags flags)
		{
			if (IsScalar)
			{
				if (Body.NodeType == ExpressionType.Parameter)
					for (var i = 0; i < Sequence.Length; i++)
						if (Body == Lambda.Parameters[i])
							return Sequence[i].ConvertToIndex(expression, level, flags);

				if (expression == null)
				{
					var member = Tuple.Create((MemberInfo)null, flags);

					SqlInfo[] idx;

					if (!_memberIndex.TryGetValue(member, out idx))
					{
						idx = ConvertToSql(expression, 0, flags);

						foreach (var info in idx)
							info.Index = GetIndex(info.Sql);

						_memberIndex.Add(member, idx);
					}

					return idx;
				}

				switch (flags)
				{
					case ConvertFlags.Field :
					case ConvertFlags.All   : return GetSequence(expression, level).ConvertToIndex(expression, level + 1, flags);
				}
			}
			else
			{
				if (expression == null)
				{
					switch (flags)
					{
						case ConvertFlags.Field : throw new NotImplementedException();
						case ConvertFlags.Key   :
						case ConvertFlags.All   :
							{
								var p = Expression.Parameter(Body.Type, "p");
								var q =
									from m in Members.Keys
									where !(m is MethodInfo)
									select ConvertToIndex(Expression.MakeMemberAccess(p, m), 1, flags) into mm
									from m in mm
									select m;

								return q.ToArray();
							}
					}
				}

				switch (flags)
				{
					case ConvertFlags.All   :
					case ConvertFlags.Key   :
					case ConvertFlags.Field :
						{
							if (level == 0)
							{
								var idx = Parser.ParseExpressions(this, expression, flags);

								foreach (var info in idx)
									info.Index = GetIndex(info.Sql);

								return idx;
							}

							var levelExpression = expression.GetLevelExpression(level);

							switch (levelExpression.NodeType)
							{
								case ExpressionType.MemberAccess :
									{
										if (levelExpression == expression)
										{
											var member = Tuple.Create(((MemberExpression)levelExpression).Member, flags);

											SqlInfo[] idx;

											if (!_memberIndex.TryGetValue(member, out idx))
											{
												idx = ConvertToSql(expression, level, flags);

												if (flags == ConvertFlags.Field && idx.Length != 1)
													throw new InvalidOperationException();

												foreach (var info in idx)
												{
													info.Index = GetIndex(info.Sql);
												}

												_memberIndex.Add(member, idx);
											}

											return idx;
										}

										return ProcessMemberAccess(
											expression,
											levelExpression,
											level,
											(n, ctx, ex, l, _) => n == 0 ?
												GetSequence(expression, level).ConvertToIndex(expression, level + 1, flags) :
												ctx.ConvertToIndex(ex, l, flags));
									}

								case ExpressionType.Parameter:

									if (levelExpression != expression)
										return GetSequence(expression, level).ConvertToIndex(expression, level + 1, flags);
									break;
							}

							break;
						}
				}
			}

			throw new NotImplementedException();
		}

		int GetIndex(ISqlExpression sql)
		{
			return SqlQuery.Select.Add(sql);
		}

		#endregion

		#region IsExpression

		public virtual bool IsExpression(Expression expression, int level, RequestFor requestFlag)
		{
			switch (requestFlag)
			{
				case RequestFor.SubQuery    : return false;
				case RequestFor.Root        :
					return Sequence.Length == 1 ?
						expression == Lambda.Parameters[0] :
						Lambda.Parameters.Any(p => p == expression);
			}

			if (IsScalar)
			{
				if (expression == null)
					return IsExpression(Body, 0, requestFlag);

				switch (requestFlag)
				{
					default                     : return false;
					case RequestFor.Association :
					case RequestFor.Field       :
					case RequestFor.Expression  :
					case RequestFor.Object       :
						return ProcessScalar(
							expression,
							level,
							(ctx, ex, l) => ctx.IsExpression(ex, l, requestFlag),
							() => requestFlag == RequestFor.Expression);
				}
			}
			else
			{
				switch (requestFlag)
				{
					default                     : return false;
					case RequestFor.Association :
					case RequestFor.Field       :
					case RequestFor.Expression  :
					case RequestFor.Object       :
						{
							if (expression == null)
							{
								if (requestFlag == RequestFor.Expression)
									return Members.Values.Any(member => IsExpression(member, 0, requestFlag));

								return requestFlag == RequestFor.Object;
							}

							var levelExpression = expression.GetLevelExpression(level);

							switch (levelExpression.NodeType)
							{
								case ExpressionType.MemberAccess :
									{
										var memberExpression = Members[((MemberExpression)levelExpression).Member];

										if (levelExpression == expression)
										{
											switch (memberExpression.NodeType)
											{
												case ExpressionType.New        :
												case ExpressionType.MemberInit : return requestFlag == RequestFor.Object;
											}
										}

										return ProcessMemberAccess(
											expression,
											levelExpression,
											level,
											(n,ctx,ex,l,_) => n == 0 ?
												requestFlag == RequestFor.Expression :
												ctx.IsExpression(ex, l, requestFlag));
									}

								case ExpressionType.Parameter    :
									{
										var sequence  = GetSequence(expression, level);
										var parameter = Lambda.Parameters[Sequence.Length == 0 ? 0 : Array.IndexOf(Sequence, sequence)];

										if (levelExpression == expression)
										{
											if (levelExpression == parameter)
												return sequence.IsExpression(null, 0, requestFlag);
										}
										else if (level == 0)
											return sequence.IsExpression(expression, 1, requestFlag);

										break;
									}

								case ExpressionType.New          :
								case ExpressionType.MemberInit   : return requestFlag == RequestFor.Object;
								default                          : return requestFlag == RequestFor.Expression;
							}

							break;
						}
				}
			}

			throw new NotImplementedException();
		}

		#endregion

		#region GetContext

		public virtual IParseContext GetContext(Expression expression, int level, SqlQuery currentSql)
		{
			if (IsScalar)
			{
				return ProcessScalar(
					expression,
					level,
					(ctx, ex, l) => ctx.GetContext(ex, l, currentSql),
					() => { throw new NotImplementedException(); });
			}
			else
			{
				var levelExpression = expression.GetLevelExpression(level);

				switch (levelExpression.NodeType)
				{
					case ExpressionType.MemberAccess :
						{
							var context = ProcessMemberAccess(
								expression,
								levelExpression,
								level,
								(n,ctx,ex,l,_) => n == 0 ?
									null :
									ctx.GetContext(ex, l, currentSql));

							if (context == null)
								throw new NotImplementedException();

							return context;
						}

					case ExpressionType.Parameter    :
						{
							var sequence  = GetSequence(expression, level);
							var parameter = Lambda.Parameters[Sequence.Length == 0 ? 0 : Array.IndexOf(Sequence, sequence)];

							if (levelExpression == expression)
							{
								if (levelExpression == parameter)
									return sequence.GetContext(null, 0, currentSql);
							}
							else if (level == 0)
								return sequence.GetContext(expression, 1, currentSql);

							break;
						}
				}

				if (level == 0)
				{
					var sequence = GetSequence(expression, level);
					return sequence.GetContext(expression, level + 1, currentSql);
				}
			}

			throw new NotImplementedException();
		}

		#endregion

		#region ConvertToParentIndex

		public virtual int ConvertToParentIndex(int index, IParseContext context)
		{
			return Parent == null ? index : Parent.ConvertToParentIndex(index, this);
		}

		#endregion

		#region SetAlias

		public virtual void SetAlias(string alias)
		{
		}

		#endregion

		#region Helpers

		T ProcessScalar<T>(Expression expression, int level, Func<IParseContext,Expression,int,T> action, Func<T> defaultAction)
		{
			if (level == 0)
			{
				if (Body.NodeType == ExpressionType.Parameter)
				{
					var sequence = GetSequence(Body, 0);

					return expression == Body ?
						action(sequence, null,       0) :
						action(sequence, expression, 1);
				}

				var levelExpression = expression.GetLevelExpression(level);

				if (levelExpression != expression)
					return action(GetSequence(expression, level), expression, level + 1);

				switch (Body.NodeType)
				{
					case ExpressionType.MemberAccess : return action(GetSequence(expression, level), null, 0);
					default                          : return defaultAction();
				}
			}
			else
			{
				var root = Body.GetRootObject();

				if (root.NodeType == ExpressionType.Parameter)
				{
					var levelExpression = expression.GetLevelExpression(level - 1);
					var parseExpression = GetParseExpression(expression, levelExpression, Body);

					return action(this, parseExpression, 0);
				}
			}

			throw new NotImplementedException();
		}

		T ProcessMemberAccess<T>(Expression expression, Expression levelExpression, int level,
			Func<int,IParseContext,Expression,int,Expression,T> action)
		{
			var memberExpression = Members[((MemberExpression)levelExpression).Member];
			var parseExpression  = GetParseExpression(expression, levelExpression, memberExpression);

			var sequence  = GetSequence(expression, level);
			var parameter = Lambda.Parameters[Sequence.Length == 0 ? 0 : Array.IndexOf(Sequence, sequence)];

			if (memberExpression == parameter && levelExpression == expression)
				return action(1, sequence, null, 0, memberExpression);

			switch (memberExpression.NodeType)
			{
				case ExpressionType.MemberAccess :
				case ExpressionType.Parameter    :
				case ExpressionType.Call         : return action(2, sequence, parseExpression, 1, memberExpression);
				case ExpressionType.New          :
				case ExpressionType.MemberInit   :
					{
						var mmExpresion = GetMemberExpression(memberExpression, expression, level + 1);
						return action(3, this, mmExpresion, 0, memberExpression);
					}
			}

			return action(0, this, null, 0, memberExpression);
		}

		protected bool IsSubQuery()
		{
			for (var p = Parent; p != null; p = p.Parent)
				if (p.IsExpression(null, 0, RequestFor.SubQuery))
					return true;
			return false;
		}

		IParseContext GetSequence(Expression expression, int level)
		{
			if (Sequence.Length == 1)
				return Sequence[0];

			var levelExpression = expression.GetLevelExpression(level);

			if (IsScalar)
			{
				var root =  expression.GetRootObject();

				if (root.NodeType == ExpressionType.Parameter)
					for (var i = 0; i < Lambda.Parameters.Count; i++)
						if (root == Lambda.Parameters[i])
							return Sequence[i];
			}
			else
			{
				switch (levelExpression.NodeType)
				{
					case ExpressionType.MemberAccess :
						{
							var memberExpression = Members[((MemberExpression)levelExpression).Member];
							var root             =  memberExpression.GetRootObject();

							for (var i = 0; i < Lambda.Parameters.Count; i++)
								if (root == Lambda.Parameters[i])
									return Sequence[i];

							break;
						}

					case ExpressionType.Parameter :
						{
							var root =  expression.GetRootObject();

							if (levelExpression == root)
							{
								for (var i = 0; i < Lambda.Parameters.Count; i++)
									if (levelExpression == Lambda.Parameters[i])
										return Sequence[i];
							}

							break;
						}
				}
			}

			throw new NotImplementedException();
		}

		static Expression GetParseExpression(Expression expression, Expression levelExpression, Expression memberExpression)
		{
			return levelExpression != expression ?
				expression.Convert(ex => ex == levelExpression ? memberExpression : ex) :
				memberExpression;
		}

		static Expression GetMemberExpression(Expression newExpression, Expression expression, int level)
		{
			var levelExpresion = expression.GetLevelExpression(level);

			switch (newExpression.NodeType)
			{
				case ExpressionType.New        :
				case ExpressionType.MemberInit : break;
				default                        :
					//if (levelExpresion == expression)
					//	return newExpression;

					var le = expression.GetLevelExpression(level - 1);

					return GetParseExpression(expression, le, newExpression);
			}

			if (levelExpresion.NodeType != ExpressionType.MemberAccess)
				throw new LinqException("Invalid expression {0}", levelExpresion);

			var me = (MemberExpression)levelExpresion;

			switch (newExpression.NodeType)
			{
				case ExpressionType.New:
					{
						var expr = (NewExpression)newExpression;

// ReSharper disable ConditionIsAlwaysTrueOrFalse
// ReSharper disable HeuristicUnreachableCode
						if (expr.Members == null)
							throw new LinqException("Invalid expression {0}", expression);
// ReSharper restore HeuristicUnreachableCode
// ReSharper restore ConditionIsAlwaysTrueOrFalse

						for (var i = 0; i < expr.Members.Count; i++)
							if (me.Member == expr.Members[i])
								return levelExpresion == expression ?
									expr.Arguments[i].Unwrap() :
									GetMemberExpression(expr.Arguments[i].Unwrap(), expression, level + 1);

						throw new LinqException("Invalid expression {0}", expression);
					}

				case ExpressionType.MemberInit:
					{
						var expr = (MemberInitExpression)newExpression;

						foreach (var binding in expr.Bindings)
						{
							if (binding is MemberAssignment)
							{
								var ma = (MemberAssignment)binding;

								if (me.Member == binding.Member)
									return levelExpresion == expression ?
										ma.Expression.Unwrap() :
										GetMemberExpression(ma.Expression.Unwrap(), expression, level + 1);
							}
							else
								throw new InvalidOperationException();
						}

						throw new LinqException("Invalid expression {0}", expression);
					}
			}

			return expression;
		}

		#endregion
	}
}
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            