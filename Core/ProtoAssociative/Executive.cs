
using System.Collections.Generic;
using System.Diagnostics;
using ProtoCore;

namespace ProtoAssociative
{

	public class Executive : ProtoCore.Executive
	{

		public Executive (Core core) : base(core)
		{
		}

        public override bool Compile(out int blockId, ProtoCore.DSASM.CodeBlock parentBlock, ProtoCore.LanguageCodeBlock langBlock, ProtoCore.CompileTime.Context callContext, ProtoCore.DebugServices.EventSink sink, ProtoCore.AST.Node codeBlockNode, ProtoCore.AssociativeGraph.GraphNode graphNode = null)
        {
            Debug.Assert(langBlock != null);
            blockId = ProtoCore.DSASM.Constants.kInvalidIndex;

            bool buildSucceeded = false;
            bool isLangSignValid = core.Langverify.Verify(langBlock);

            if (isLangSignValid)
            {
                try
                {
                    ProtoCore.CodeGen oldCodegen = core.assocCodegen;

                    if (ProtoCore.DSASM.InterpreterMode.kNormal == core.ExecMode)
                    {
                        if ((core.IsParsingPreloadedAssembly || core.IsParsingCodeBlockNode) && parentBlock == null)
                        {
                            if (core.CodeBlockList.Count == 0)
                            {
                                core.assocCodegen = new ProtoAssociative.CodeGen(core, parentBlock);
                            }
                            else 
                            {
                                // We reuse the existing toplevel CodeBlockList's for the procedureTable's 
                                // by calling this overloaded constructor - pratapa
                                core.assocCodegen = new ProtoAssociative.CodeGen(core);
                            }
                        }
                        else
                            core.assocCodegen = new ProtoAssociative.CodeGen(core, parentBlock);
                    }

                    if (null != core.AssocNode)
                    {
                        ProtoCore.AST.AssociativeAST.CodeBlockNode cnode = new ProtoCore.AST.AssociativeAST.CodeBlockNode();
                        cnode.Body.Add(core.AssocNode as ProtoCore.AST.AssociativeAST.AssociativeNode);

                        blockId = core.assocCodegen.Emit((cnode as ProtoCore.AST.AssociativeAST.CodeBlockNode), graphNode);
                    }
                    else
                    {
                        //if not null, Compile has been called from DfsTraverse. No parsing is needed. 
                        if (codeBlockNode == null) 
                        {
                            System.IO.MemoryStream memstream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(langBlock.body));
                            ProtoCore.DesignScriptParser.Scanner s = new ProtoCore.DesignScriptParser.Scanner(memstream);
                            ProtoCore.DesignScriptParser.Parser p = new ProtoCore.DesignScriptParser.Parser(s, core, core.builtInsLoaded);
                            p.Parse();

                            // TODO Jun: Set this flag inside a persistent object
                            core.builtInsLoaded = true;

                            codeBlockNode = p.root;

                            //core.AstNodeList = p.GetParsedASTList(codeBlockNode as ProtoCore.AST.AssociativeAST.CodeBlockNode);
                            List<ProtoCore.AST.Node> astNodes = ProtoCore.Utils.ParserUtils.GetAstNodes(codeBlockNode);
                            core.AstNodeList = astNodes;

                            // TODO: This is a temporary check to filter out function definition and class declaration nodes from CodeBlockNode's 
                            // in the GraphUI. This should be removed once we start allowing them - pratapa
                            // Consider moving these checks to CompileExpression()
                            /*if (core.IsParsingCodeBlockNode)
                            {
                                if (core.AstNodeList != null)
                                {
                                    foreach (ProtoCore.AST.Node node in core.AstNodeList)
                                    {
                                        ProtoCore.AST.AssociativeAST.AssociativeNode n = node as ProtoCore.AST.AssociativeAST.AssociativeNode;
                                        ProtoCore.Utils.Validity.Assert(n != null);

                                        if (n is ProtoCore.AST.AssociativeAST.FunctionDefinitionNode ||
                                            n is ProtoCore.AST.AssociativeAST.ClassDeclNode)
                                        {
                                            core.BuildStatus.LogSemanticError("Class and function definitions are not supported");
                                            throw new ProtoCore.BuildHaltException("Class and function definitions are not supported");
                                        }
                                        else if (n is ProtoCore.AST.AssociativeAST.ImportNode)
                                        {
                                            core.BuildStatus.LogSemanticError("Import statements are not supported in CodeBlock Nodes");
                                            throw new ProtoCore.BuildHaltException(
                                                "Import statements are not supported in CodeBlock Nodes. To import a file, please use the Library utility");
                                        }
                                    }
                                }
                            }*/
                        }

                        core.assocCodegen.context = callContext;

                        //Temporarily change the code block for code gen to the current block, in the case it is an imperative block
                        //CodeGen for ProtoImperative is modified to passing in the core object.
                        ProtoCore.DSASM.CodeBlock oldCodeBlock = core.assocCodegen.codeBlock;
                        if (core.ExecMode == ProtoCore.DSASM.InterpreterMode.kExpressionInterpreter)
                        {
                            int tempBlockId = core.GetCurrentBlockId();

                            ProtoCore.DSASM.CodeBlock tempCodeBlock = core.GetCodeBlock(core.CodeBlockList, tempBlockId);
                            while (null != tempCodeBlock && tempCodeBlock.blockType != ProtoCore.DSASM.CodeBlockType.kLanguage)
                            {
                                tempCodeBlock = tempCodeBlock.parent;
                            }
                            core.assocCodegen.codeBlock = tempCodeBlock;
                        }
                        core.assocCodegen.codeBlock.EventSink = sink;
                        if (core.BuildStatus.Errors.Count == 0) //if there is syntax error, no build needed
                        {
                             blockId = core.assocCodegen.Emit((codeBlockNode as ProtoCore.AST.AssociativeAST.CodeBlockNode), graphNode);
                        }
                        if (core.ExecMode == ProtoCore.DSASM.InterpreterMode.kExpressionInterpreter)
                        {
                            blockId = core.assocCodegen.codeBlock.codeBlockId;
                            //Restore the code block.
                            core.assocCodegen.codeBlock = oldCodeBlock;
                        }
                    }

                    // @keyu: we have to restore asscoCodegen here. It may be 
                    // reused later on. Suppose for an inline expression 
                    // "x = 1 == 2 ? 3 : 4", we dynamically create assocCodegen
                    // to compile true and false expression in this inline 
                    // expression, and if we don't restore assocCodegen, the pc
                    // is totally messed up.
                    //
                    // But if directly replace with old assocCodegen, will it
                    // replace some other useful information? Need to revisit it.
                    //
                    // Also refer to defect IDE-2120.
                    if (oldCodegen != null && core.assocCodegen != oldCodegen)
                    {
                        core.assocCodegen = oldCodegen;
                    }
                }
                catch (ProtoCore.BuildHaltException e)
                {
#if DEBUG
                    //core.BuildStatus.LogSemanticError(e.errorMsg);
#endif
                }

                int errors = 0;
                int warnings = 0;
                buildSucceeded = core.BuildStatus.GetBuildResult(out errors, out warnings);
            }

            return buildSucceeded;

        }

        public override ProtoCore.DSASM.StackValue Execute(int codeblock, int entry, ProtoCore.Runtime.Context callContext, ProtoCore.DebugServices.EventSink sink)
        {
            ProtoCore.DSASM.StackValue sv = new ProtoCore.DSASM.StackValue();
            if (!core.Options.CompileToLib)
            {
                ProtoCore.DSASM.Interpreter interpreter = new ProtoCore.DSASM.Interpreter(core);
                CurrentDSASMExec = interpreter.runtime;
                sv = interpreter.Run(codeblock, entry, Language.kAssociative);
            }
            return sv;
        }

        public override ProtoCore.DSASM.StackValue Execute(int codeblock, int entry, ProtoCore.Runtime.Context callContext, System.Collections.Generic.List<ProtoCore.DSASM.Instruction> breakpoints, ProtoCore.DebugServices.EventSink sink = null, bool fepRun = false)
        {
            ProtoCore.DSASM.Interpreter interpreter = new ProtoCore.DSASM.Interpreter(core, fepRun);
            CurrentDSASMExec = interpreter.runtime;
            ProtoCore.DSASM.StackValue sv = interpreter.Run(breakpoints, codeblock, entry, Language.kAssociative);
            return sv;
        }

	}
}

